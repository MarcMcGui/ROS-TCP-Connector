# ROS2 Actions Enhancement Guide

This document describes the new ROS2 actions features added to the ROSConnection system.

## ⚠️ CRITICAL: Pre-Register Message Types

**Before using any action for the first time**, you MUST call `PreRegisterActionTypes<T, T, T>()`. This ensures the ROS-TCP-Endpoint has imported and validated your message types before you try to send them.

**Failure to do this will result in errors like:**
```
Failed to resolve your_msgs/YourGoal: module 'your_msgs.msg' has no attribute 'YourGoal'
Not registered to publish topic '/your_action'!
```

**Quick Fix:**
```csharp
void Start()
{
    var ros = ROSConnection.GetOrCreateInstance();
    
    // FIRST: Pre-register all message types
    ros.PreRegisterActionTypes<FibonacciGoal, FibonacciFeedback, FibonacciResult>("/fibonacci");
    
    // THEN: Create your client/server
    m_ActionClient = ros.CreateActionClient<FibonacciGoal, FibonacciFeedback, FibonacciResult>(
        "/fibonacci",
        "example_interfaces/Fibonacci");
}
```

This single method call prevents all endpoint message resolution errors.

## New Classes

### 1. **GoalHandle<TGoal, TFeedback, TResult>**

A handle representing a single goal in an action. Tracks the goal's state, feedback, and result.

**Key Features:**
- `GoalId`: Unique identifier for the goal
- `Status`: Current status (Pending, Active, Succeeded, Aborted, etc.)
- `IsActive`: Whether the goal is still being processed
- `IsTerminalState`: Whether the goal has completed
- `Goal`: The original goal message
- `LastFeedback`: Most recent feedback message
- `Result`: Final result message

**Events:**
- `FeedbackReceived(goalId, feedback)`: Fired when feedback arrives
- `ResultReceived(goalId, status, result)`: Fired when result arrives
- `CancelRequested(goalId)`: Fired when cancellation is requested

### 2. **RosActionClient<TGoal, TFeedback, TResult>**

Enhanced client for sending goals to action servers. **Now returns GoalHandle objects instead of strings.**

**Key Methods:**

```csharp
// Send a goal and get a handle to track it
GoalHandle<TGoal, TFeedback, TResult> handle = actionClient.SendGoal(goal);

// Wait for result asynchronously
var (status, result) = await actionClient.SendGoalAsync(goal);

// Get a goal by ID
var handle = actionClient.GetGoal(goalId);

// Check if goal is active
bool active = actionClient.IsGoalActive(goalId);

// Get goal status
GoalStatus? status = actionClient.GetGoalStatus(goalId);

// Get all active goals
var allGoals = actionClient.GetActiveGoals();

// Cancel a specific goal
actionClient.CancelGoal(goalId);

// Cancel all goals
actionClient.CancelAllGoals();
```

### 3. **RosActionServer<TGoal, TFeedback, TResult>**

Helper class for implementing action servers in Unity.

**Key Methods:**

```csharp
// Register the server with callbacks
server.RegisterServer(
    onGoalReceived: (goalId, goal) =>
    {
        var handle = new GoalHandle<TGoal, TFeedback, TResult>(goalId, goal);
        // Start processing...
        return handle;
    },
    onCancelRequested: (goalId) =>
    {
        // Handle cancellation request
    }
);

// Send feedback while processing
server.PublishFeedback(goalId, feedbackMsg);

// Complete the goal successfully
server.Succeed(goalId, resultMsg);

// Abort the goal
server.Abort(goalId, resultMsg);

// Cancel the goal
server.Cancel(goalId, resultMsg);

// Update goal status
server.UpdateGoalStatus(goalId, GoalStatus.Active);

// Query goals
var goal = server.GetGoal(goalId);
var allGoals = server.GetActiveGoals();
```

### 4. **GoalStatus Enum**

```csharp
public enum GoalStatus
{
    Unknown = -1,
    Pending = 0,      // Not yet processed
    Active = 1,       // Currently executing
    Preempted = 2,    // Interrupted by new goal
    Succeeded = 3,    // Completed successfully
    Aborted = 4,      // Failed during execution
    Canceled = 5,     // Cancelled by client
    Rejected = 6,     // Rejected before execution
}
```

## New ROSConnection Methods

```csharp
// Implement an action server
public void ImplementRosActionServer<TGoal, TResult, TFeedback>(
    string actionName,
    Func<string, TGoal, GoalHandle<TGoal, TFeedback, TResult>> onGoalReceived,
    Action<string> onCancelRequested = null)

// Send feedback from a server
public void SendActionFeedback<TFeedback>(string actionName, string goalId, TFeedback feedback)

// Send result from a server
public void SendActionResult<TResult>(string actionName, string goalId, GoalStatus status, TResult result)

// Update goal status
public void UpdateActionGoalStatus(string actionName, string goalId, GoalStatus status)
```

## Usage Examples

### Example 1: Action Client with GoalHandle

```csharp
public class MyActionClient : MonoBehaviour
{
    RosActionClient<FibonacciGoal, FibonacciFeedback, FibonacciResult> m_ActionClient;

    void Start()
    {
        var ros = ROSConnection.GetOrCreateInstance();
        m_ActionClient = ros.CreateActionClient<FibonacciGoal, FibonacciFeedback, FibonacciResult>(
            "/fibonacci",
            "example_interfaces/Fibonacci");
    }

    public void SendFibonacciGoal()
    {
        var goal = new FibonacciGoal { order = 10 };
        var handle = m_ActionClient.SendGoal(goal);

        // Listen to feedback
        handle.FeedbackReceived += (goalId, feedback) =>
        {
            Debug.Log($"Goal {goalId} feedback: {feedback.sequence.Length}");
        };

        // Listen to result
        handle.ResultReceived += (goalId, status, result) =>
        {
            Debug.Log($"Goal {goalId} completed with status {status}");
            Debug.Log($"Final sequence length: {result.sequence.Length}");
        };

        // Listen to cancellation
        handle.CancelRequested += (goalId) =>
        {
            Debug.Log($"Goal {goalId} was cancelled");
        };
    }

    public async void SendGoalAndWait()
    {
        var goal = new FibonacciGoal { order = 10 };
        
        try
        {
            var (status, result) = await m_ActionClient.SendGoalAsync(goal);
            Debug.Log($"Goal completed with status {status}");
            Debug.Log($"Result: {result.sequence.Length} numbers");
        }
        catch (System.TimeoutException ex)
        {
            Debug.LogError($"Goal timed out: {ex.Message}");
        }
    }

    public void CancelAllGoals()
    {
        m_ActionClient.CancelAllGoals();
    }
}
```

### Example 2: Action Server Implementation

```csharp
public class MyActionServer : MonoBehaviour
{
    RosActionServer<FibonacciGoal, FibonacciFeedback, FibonacciResult> m_ActionServer;

    void Start()
    {
        var ros = ROSConnection.GetOrCreateInstance();
        
        m_ActionServer = new RosActionServer<FibonacciGoal, FibonacciFeedback, FibonacciResult>(
            ros,
            "/fibonacci_server",
            "example_interfaces/Fibonacci");

        m_ActionServer.RegisterServer(
            onGoalReceived: HandleGoal,
            onCancelRequested: HandleCancelRequest);
    }

    GoalHandle<FibonacciGoal, FibonacciFeedback, FibonacciResult> HandleGoal(
        string goalId, 
        FibonacciGoal goal)
    {
        Debug.Log($"Received goal {goalId}: compute {goal.order} fibonacci numbers");

        var handle = m_ActionServer.GetGoal(goalId);
        if (handle == null)
        {
            handle = new GoalHandle<FibonacciGoal, FibonacciFeedback, FibonacciResult>(goalId, goal);
        }

        // Start processing in a coroutine
        StartCoroutine(ProcessGoal(goalId, goal, handle));

        return handle;
    }

    void HandleCancelRequest(string goalId)
    {
        Debug.Log($"Cancel requested for goal {goalId}");
        // Stop processing the goal
        StopCoroutine(ProcessGoal(goalId, null, null));
    }

    IEnumerator ProcessGoal(
        string goalId,
        FibonacciGoal goal,
        GoalHandle<FibonacciGoal, FibonacciFeedback, FibonacciResult> handle)
    {
        var sequence = new List<int> { 0, 1 };
        
        m_ActionServer.UpdateGoalStatus(goalId, GoalStatus.Active);

        for (int i = 2; i < goal.order; i++)
        {
            if (handle != null && !handle.IsActive)
            {
                // Goal was cancelled
                break;
            }

            sequence.Add(sequence[i - 1] + sequence[i - 2]);

            // Send periodic feedback
            if (i % 5 == 0)
            {
                var feedback = new FibonacciFeedback
                {
                    sequence = sequence.ToArray()
                };
                m_ActionServer.PublishFeedback(goalId, feedback);
            }

            yield return new WaitForSeconds(0.1f);
        }

        // Send final result
        var result = new FibonacciResult
        {
            sequence = sequence.ToArray()
        };

        if (handle?.IsActive ?? false)
        {
            m_ActionServer.Succeed(goalId, result);
        }
    }

    void OnDestroy()
    {
        m_ActionServer?.Dispose();
    }
}
```

### Example 3: Checking Goal Status

```csharp
void CheckGoalStatus()
{
    // Get status of specific goal
    var status = m_ActionClient.GetGoalStatus(goalId);
    if (status == GoalStatus.Active)
    {
        Debug.Log("Goal is still processing");
    }

    // Get all active goals
    var activeGoals = m_ActionClient.GetActiveGoals();
    foreach (var goal in activeGoals)
    {
        Debug.Log($"Goal {goal.GoalId}: {GoalHandle<TGoal, TFeedback, TResult>.StatusToString(goal.Status)}");
    }

    // Check if goal reached terminal state
    var handle = m_ActionClient.GetGoal(goalId);
    if (handle?.IsTerminalState ?? false)
    {
        Debug.Log($"Goal completed with result: {handle.Result}");
    }
}
```

## Breaking Changes

⚠️ **Important:** The `SendGoal()` method in `RosActionClient` now returns a `GoalHandle<TGoal, TFeedback, TResult>` instead of a string.

### Before:
```csharp
string goalId = actionClient.SendGoal(goal);
```

### After:
```csharp
var handle = actionClient.SendGoal(goal);
string goalId = handle.GoalId;  // If you need the ID
```

The legacy `FeedbackReceived` and `ResultReceived` events are marked as `[Obsolete]` but still supported for backwards compatibility.

## Migration Guide

### For Existing Client Code

```csharp
// Old way (still works but obsolete)
actionClient.FeedbackReceived += (goalId, feedback) => { /*...*/ };
actionClient.ResultReceived += (goalId, result) => { /*...*/ };

// New way (recommended)
var handle = actionClient.SendGoal(goal);
handle.FeedbackReceived += (goalId, feedback) => { /*...*/ };
handle.ResultReceived += (goalId, status, result) => { /*...*/ };
```

### For New Server Code

The `ImplementRosActionServer` method in `ROSConnection` is now functional. Use either:

1. **Direct ROSConnection method** (lower-level):
```csharp
ros.ImplementRosActionServer<TGoal, TResult, TFeedback>(
    actionName,
    onGoalReceived: (goalId, goal) => { /*...*/ });
```

2. **RosActionServer helper class** (recommended, higher-level):
```csharp
var server = new RosActionServer<TGoal, TFeedback, TResult>(ros, actionName, actionType);
server.RegisterServer(
    onGoalReceived: (goalId, goal) => { /*...*/ });
```

## Thread Safety

All collections are protected with locks:
- `m_ActiveGoals` in `RosActionClient`
- `m_GoalHandles` in `RosActionServer`
- `m_ActionServerHandlers` in `ROSConnection`

Safe to call from multiple threads.

## Future Enhancements

Potential improvements for future versions:
- Automatic timeout handling for goals
- Goal preemption helpers
- Status persistence/history
- Advanced goal filtering and querying
- Performance metrics per goal
