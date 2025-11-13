# Type Inference Fix Summary

## Problem
Compilation error: 
```
CS0411: The type arguments for method 'ROSConnection.ImplementRosActionServer<TGoal, TResult, TFeedback>(string, Func<string, TGoal, GoalHandle<TGoal, TFeedback, TResult>>, Action<string>)' cannot be inferred from the usage.
```

## Root Cause
There was a **type signature mismatch** between:

1. **RosActionServer.RegisterServer()** 
   - Creates a handler: `Func<string, byte[], GoalHandle<TGoal, TFeedback, TResult>>`
   - Takes raw bytes and deserializes them internally

2. **ROSConnection.ImplementRosActionServer<>()** (BEFORE)
   - Expected: `Func<string, TGoal, GoalHandle<TGoal, TFeedback, TResult>>`
   - Expected deserialized TGoal object

3. **ROSConnection handler invocation** (in ReceiveSysCommand)
   - Calls: `method.DynamicInvoke(info.goal_id, payload)` 
   - Passes raw `payload` bytes, not deserialized TGoal

## Solution

### Change 1: Updated ROSConnection.ImplementRosActionServer signature
**File:** `ROSConnection.cs` (line 539)

```csharp
// BEFORE
public void ImplementRosActionServer<TGoal, TResult, TFeedback>(
    string actionName,
    Func<string, TGoal, GoalHandle<TGoal, TFeedback, TResult>> onGoalReceived,
    Action<string> onCancelRequested = null)

// AFTER
public void ImplementRosActionServer<TGoal, TResult, TFeedback>(
    string actionName,
    Func<string, byte[], GoalHandle<TGoal, TFeedback, TResult>> onGoalReceived,
    Action<string> onCancelRequested = null)
```

**Reason:** The handler is invoked with raw bytes from the network, not deserialized objects. The deserialization happens inside the wrapped handler in RosActionServer.

### Change 2: Explicitly specify type arguments in RosActionServer
**File:** `RosActionServer.cs` (line 92)

```csharp
// BEFORE
m_Connection.ImplementRosActionServer(
    m_ActionName,
    wrappedHandler,
    m_OnCancelRequested);

// AFTER  
m_Connection.ImplementRosActionServer<TGoal, TResult, TFeedback>(
    m_ActionName,
    wrappedHandler,
    m_OnCancelRequested);
```

**Reason:** Explicitly specifying the generic type arguments helps the C# compiler resolve the generic method call unambiguously.

## Architecture

The flow is now:
1. Raw bytes received from network → `ROSConnection.__action_goal_request`
2. `ROSConnection` invokes `onGoalReceived(goalId, rawBytes)` 
3. `RosActionServer`'s wrapped handler deserializes bytes → `TGoal`
4. Wrapped handler calls user's `RegisterServer(onGoalReceived)` callback with deserialized `TGoal`
5. User callback returns `GoalHandle<TGoal, TFeedback, TResult>`

## Files Modified
- `com.unity.robotics.ros-tcp-connector/Runtime/TcpConnector/ROSConnection.cs`
- `com.unity.robotics.ros-tcp-connector/Runtime/TcpConnector/RosActionServer.cs`
