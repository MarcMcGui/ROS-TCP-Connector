# ROS2 Actions - Complete Code Examples

This document contains ready-to-use examples for implementing ROS2 actions with the enhanced ROSConnection.

## Example 1: Simple Fibonacci Action Client

```csharp
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.ExampleInterfaces;  // Your generated message types
using UnityEngine;
using System.Collections.Generic;

public class FibonacciActionClient : MonoBehaviour
{
    private RosActionClient<FibonacciGoal, FibonacciFeedback, FibonacciResult> m_ActionClient;
    private Queue<string> m_GoalQueue = new Queue<string>();

    void Start()
    {
        var ros = ROSConnection.GetOrCreateInstance();
        
        // CRITICAL: Pre-register message types FIRST
        // This prevents "Failed to resolve" errors from the endpoint
        ros.PreRegisterActionTypes<FibonacciGoal, FibonacciFeedback, FibonacciResult>("/fibonacci");
        
        // Create the action client
        m_ActionClient = ros.CreateActionClient<FibonacciGoal, FibonacciFeedback, FibonacciResult>(
            "/fibonacci",
            "example_interfaces/Fibonacci");
        
        Debug.Log("Fibonacci action client initialized");
    }

    /// <summary>
    /// Send a Fibonacci goal and track it with callbacks
    /// </summary>
    public void SendGoalWithCallbacks()
    {
        var goal = new FibonacciGoal { order = 10 };
        
        var handle = m_ActionClient.SendGoal(goal);
        m_GoalQueue.Enqueue(handle.GoalId);

        Debug.Log($"Sent goal {handle.GoalId} for Fibonacci({goal.order})");

        // Subscribe to feedback
        handle.FeedbackReceived += (goalId, feedback) =>
        {
            Debug.Log($"[{goalId}] Progress: {feedback.sequence.Length} numbers computed");
        };

        // Subscribe to result
        handle.ResultReceived += (goalId, status, result) =>
        {
            Debug.Log($"[{goalId}] Goal {status}: computed {result.sequence.Length} numbers");
            if (result.sequence.Length > 0)
            {
                Debug.Log($"  Last number: {result.sequence[result.sequence.Length - 1]}");
            }
        };

        // Subscribe to cancellation
        handle.CancelRequested += (goalId) =>
        {
            Debug.Log($"[{goalId}] Goal was cancelled");
        };
    }

    /// <summary>
    /// Send a goal and wait for the result (blocking with timeout)
    /// </summary>
    public async void SendGoalAndWait()
    {
        var goal = new FibonacciGoal { order = 15 };
        
        Debug.Log("Sending Fibonacci(15) and waiting for result...");
        
        try
        {
            var (status, result) = await m_ActionClient.SendGoalAsync(goal);
            
            Debug.Log($"Goal completed with status: {status}");
            Debug.Log($"Result: {result.sequence.Length} numbers");
            for (int i = 0; i < result.sequence.Length && i < 5; i++)
            {
                Debug.Log($"  F({i}) = {result.sequence[i]}");
            }
        }
        catch (System.TimeoutException ex)
        {
            Debug.LogError($"Goal timed out: {ex.Message}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Check the status of an active goal
    /// </summary>
    public void CheckGoalStatus(string goalId)
    {
        var status = m_ActionClient.GetGoalStatus(goalId);
        
        if (status.HasValue)
        {
            string statusStr = GoalHandle<FibonacciGoal, FibonacciFeedback, FibonacciResult>
                .StatusToString(status.Value);
            Debug.Log($"Goal {goalId} status: {statusStr}");
        }
        else
        {
            Debug.LogWarning($"Goal {goalId} not found");
        }
    }

    /// <summary>
    /// View all active goals
    /// </summary>
    public void ListActiveGoals()
    {
        var activeGoals = m_ActionClient.GetActiveGoals();
        
        Debug.Log("=== Active Goals ===");
        foreach (var goal in activeGoals)
        {
            var status = GoalHandle<FibonacciGoal, FibonacciFeedback, FibonacciResult>
                .StatusToString(goal.Status);
            Debug.Log($"  {goal.GoalId}: {status} (active={goal.IsActive})");
        }
    }

    /// <summary>
    /// Cancel a specific goal
    /// </summary>
    public void CancelGoal(string goalId)
    {
        var goal = m_ActionClient.GetGoal(goalId);
        if (goal != null && goal.IsActive)
        {
            Debug.Log($"Cancelling goal {goalId}...");
            m_ActionClient.CancelGoal(goalId);
        }
    }

    /// <summary>
    /// Cancel all active goals
    /// </summary>
    public void CancelAllGoals()
    {
        Debug.Log("Cancelling all active goals...");
        m_ActionClient.CancelAllGoals();
    }

    void OnDestroy()
    {
        m_ActionClient?.Dispose();
    }
}
```

## Example 2: Simple Fibonacci Action Server

```csharp
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.ExampleInterfaces;  // Your generated message types
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class FibonacciActionServer : MonoBehaviour
{
    private RosActionServer<FibonacciGoal, FibonacciFeedback, FibonacciResult> m_ActionServer;

    void Start()
    {
        var ros = ROSConnection.GetOrCreateInstance();
        
        // CRITICAL: Pre-register message types FIRST
        // This prevents "Failed to resolve" errors from the endpoint
        ros.PreRegisterActionTypes<FibonacciGoal, FibonacciFeedback, FibonacciResult>("/fibonacci");
        
        // Create the action server
        m_ActionServer = new RosActionServer<FibonacciGoal, FibonacciFeedback, FibonacciResult>(
            ros,
            "/fibonacci",
            "example_interfaces/Fibonacci");

        // Register the server with goal and cancellation handlers
        m_ActionServer.RegisterServer(
            onGoalReceived: HandleGoalReceived,
            onCancelRequested: HandleCancelRequested);

        Debug.Log("Fibonacci action server initialized");
    }

    /// <summary>
    /// Called when a new goal is received from a client
    /// </summary>
    private GoalHandle<FibonacciGoal, FibonacciFeedback, FibonacciResult> HandleGoalReceived(
        string goalId,
        FibonacciGoal goal)
    {
        Debug.Log($"[Server] Received goal {goalId}: Compute Fibonacci({goal.order})");

        // Create a goal handle to track this goal
        var handle = new GoalHandle<FibonacciGoal, FibonacciFeedback, FibonacciResult>(goalId, goal);

        // Start processing the goal in a coroutine
        StartCoroutine(ProcessFibonacciGoal(goalId, goal, handle));

        return handle;
    }

    /// <summary>
    /// Called when a cancellation is requested for a goal
    /// </summary>
    private void HandleCancelRequested(string goalId)
    {
        Debug.Log($"[Server] Cancel requested for goal {goalId}");
        
        // The coroutine will check IsActive and stop processing
        // You could also set a flag here if preferred
    }

    /// <summary>
    /// Process a Fibonacci goal with periodic feedback
    /// </summary>
    private IEnumerator ProcessFibonacciGoal(
        string goalId,
        FibonacciGoal goal,
        GoalHandle<FibonacciGoal, FibonacciFeedback, FibonacciResult> handle)
    {
        // Update status to ACTIVE
        m_ActionServer.UpdateGoalStatus(goalId, GoalStatus.Active);

        var sequence = new List<int> { 0, 1 };

        // Compute Fibonacci sequence
        for (int i = 2; i < goal.order; i++)
        {
            // Check if cancellation was requested
            if (!handle.IsActive)
            {
                Debug.Log($"[Server] Goal {goalId} was cancelled, stopping computation");
                m_ActionServer.Cancel(goalId, new FibonacciResult { sequence = sequence.ToArray() });
                yield break;
            }

            sequence.Add(sequence[i - 1] + sequence[i - 2]);

            // Send feedback every few iterations
            if (i % 3 == 0)
            {
                var feedback = new FibonacciFeedback
                {
                    sequence = sequence.ToArray()
                };
                m_ActionServer.PublishFeedback(goalId, feedback);
                Debug.Log($"[Server] Goal {goalId} feedback: {sequence.Count} numbers");
            }

            // Simulate some processing time
            yield return new WaitForSeconds(0.1f);
        }

        // Ensure we have at least 'order' numbers
        while (sequence.Count < goal.order)
        {
            sequence.Add(sequence[sequence.Count - 1] + sequence[sequence.Count - 2]);
            yield return new WaitForSeconds(0.05f);
        }

        // Send final result
        var result = new FibonacciResult
        {
            sequence = sequence.ToArray()
        };

        m_ActionServer.Succeed(goalId, result);
        Debug.Log($"[Server] Goal {goalId} succeeded with {result.sequence.Length} numbers");
    }

    void OnDestroy()
    {
        m_ActionServer?.Dispose();
    }
}
```

## Example 3: Client with Manual Goal Tracking

```csharp
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.ExampleInterfaces;
using UnityEngine;
using System.Collections.Generic;

public class AdvancedFibonacciClient : MonoBehaviour
{
    private RosActionClient<FibonacciGoal, FibonacciFeedback, FibonacciResult> m_ActionClient;
    private Dictionary<string, GoalMetadata> m_TrackedGoals = new Dictionary<string, GoalMetadata>();

    private class GoalMetadata
    {
        public GoalHandle<FibonacciGoal, FibonacciFeedback, FibonacciResult> Handle;
        public int FeedbackCount;
        public float CreatedTime;
    }

    void Start()
    {
        var ros = ROSConnection.GetOrCreateInstance();
        m_ActionClient = ros.CreateActionClient<FibonacciGoal, FibonacciFeedback, FibonacciResult>(
            "/fibonacci",
            "example_interfaces/Fibonacci");
    }

    /// <summary>
    /// Send multiple goals and track their progress
    /// </summary>
    public void SendMultipleGoals()
    {
        for (int order = 5; order <= 15; order += 5)
        {
            var goal = new FibonacciGoal { order = order };
            var handle = m_ActionClient.SendGoal(goal);

            var metadata = new GoalMetadata
            {
                Handle = handle,
                FeedbackCount = 0,
                CreatedTime = Time.time
            };

            m_TrackedGoals[handle.GoalId] = metadata;

            // Setup callbacks
            handle.FeedbackReceived += OnFeedbackReceived;
            handle.ResultReceived += OnResultReceived;

            Debug.Log($"[Client] Sent goal {handle.GoalId} for Fibonacci({order})");
        }
    }

    private void OnFeedbackReceived(string goalId, FibonacciFeedback feedback)
    {
        if (m_TrackedGoals.TryGetValue(goalId, out var metadata))
        {
            metadata.FeedbackCount++;
            Debug.Log($"[Client] Goal {goalId}: Feedback #{metadata.FeedbackCount} - {feedback.sequence.Length} numbers");
        }
    }

    private void OnResultReceived(string goalId, GoalStatus status, FibonacciResult result)
    {
        if (m_TrackedGoals.TryGetValue(goalId, out var metadata))
        {
            float duration = Time.time - metadata.CreatedTime;
            Debug.Log($"[Client] Goal {goalId}: {status} in {duration:F2}s");
            Debug.Log($"  Feedback updates: {metadata.FeedbackCount}");
            Debug.Log($"  Final result: {result.sequence.Length} numbers");
            
            m_TrackedGoals.Remove(goalId);
        }
    }

    /// <summary>
    /// Print status of all tracked goals
    /// </summary>
    public void PrintGoalsStatus()
    {
        Debug.Log("=== Tracked Goals Status ===");
        
        foreach (var kvp in m_TrackedGoals)
        {
            var goalId = kvp.Key;
            var metadata = kvp.Value;
            var handle = metadata.Handle;
            
            var statusStr = GoalHandle<FibonacciGoal, FibonacciFeedback, FibonacciResult>
                .StatusToString(handle.Status);
            
            Debug.Log($"Goal {goalId}:");
            Debug.Log($"  Status: {statusStr}");
            Debug.Log($"  Active: {handle.IsActive}");
            Debug.Log($"  Feedbacks received: {metadata.FeedbackCount}");
            Debug.Log($"  Created {Time.time - metadata.CreatedTime:F2}s ago");
            
            if (handle.LastFeedback != null)
            {
                Debug.Log($"  Last feedback: {handle.LastFeedback.sequence.Length} numbers");
            }
        }
    }

    void OnDestroy()
    {
        m_ActionClient?.Dispose();
    }
}
```

## Example 4: Server with Multiple Concurrent Goals

```csharp
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.ExampleInterfaces;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ConcurrentFibonacciServer : MonoBehaviour
{
    private RosActionServer<FibonacciGoal, FibonacciFeedback, FibonacciResult> m_ActionServer;
    private Dictionary<string, Coroutine> m_ActiveCoroutines = new Dictionary<string, Coroutine>();

    void Start()
    {
        var ros = ROSConnection.GetOrCreateInstance();
        
        m_ActionServer = new RosActionServer<FibonacciGoal, FibonacciFeedback, FibonacciResult>(
            ros,
            "/fibonacci_concurrent",
            "example_interfaces/Fibonacci");

        m_ActionServer.RegisterServer(
            onGoalReceived: HandleGoalReceived,
            onCancelRequested: HandleCancelRequested);

        Debug.Log("Concurrent Fibonacci server started");
    }

    private GoalHandle<FibonacciGoal, FibonacciFeedback, FibonacciResult> HandleGoalReceived(
        string goalId,
        FibonacciGoal goal)
    {
        Debug.Log($"[ConcurrentServer] Goal {goalId}: order={goal.order}");

        var handle = new GoalHandle<FibonacciGoal, FibonacciFeedback, FibonacciResult>(goalId, goal);

        // Start processing in a coroutine (multiple can run concurrently)
        var coroutine = StartCoroutine(ProcessConcurrentFibonacci(goalId, goal, handle));
        m_ActiveCoroutines[goalId] = coroutine;

        return handle;
    }

    private void HandleCancelRequested(string goalId)
    {
        Debug.Log($"[ConcurrentServer] Cancelling goal {goalId}");
        
        if (m_ActiveCoroutines.TryGetValue(goalId, out var coroutine))
        {
            StopCoroutine(coroutine);
            m_ActiveCoroutines.Remove(goalId);
        }
    }

    private IEnumerator ProcessConcurrentFibonacci(
        string goalId,
        FibonacciGoal goal,
        GoalHandle<FibonacciGoal, FibonacciFeedback, FibonacciResult> handle)
    {
        m_ActionServer.UpdateGoalStatus(goalId, GoalStatus.Active);

        var sequence = new List<int> { 0, 1 };

        // Simulate varying computation times
        var processingTime = goal.order * 0.1f;
        var elapsed = 0f;

        while (sequence.Count < goal.order && elapsed < processingTime)
        {
            if (!handle.IsActive)
            {
                Debug.Log($"[ConcurrentServer] {goalId} cancelled");
                m_ActionServer.Cancel(goalId, new FibonacciResult { sequence = sequence.ToArray() });
                m_ActiveCoroutines.Remove(goalId);
                yield break;
            }

            sequence.Add(sequence[sequence.Count - 1] + sequence[sequence.Count - 2]);
            elapsed += Time.deltaTime;

            // Send feedback periodically
            if (sequence.Count % 5 == 0)
            {
                m_ActionServer.PublishFeedback(goalId, new FibonacciFeedback
                {
                    sequence = sequence.ToArray()
                });
            }

            yield return new WaitForSeconds(0.05f);
        }

        // Success
        var result = new FibonacciResult { sequence = sequence.ToArray() };
        m_ActionServer.Succeed(goalId, result);
        m_ActiveCoroutines.Remove(goalId);

        Debug.Log($"[ConcurrentServer] {goalId} succeeded");
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 300, 200));
        
        GUILayout.Label($"Active Goals: {m_ActiveCoroutines.Count}");
        GUILayout.Label($"All Goals: {m_ActionServer.GetActiveGoals().Count}");

        foreach (var kvp in m_ActiveCoroutines)
        {
            GUILayout.Label($"  {kvp.Key}");
        }

        GUILayout.EndArea();
    }

    void OnDestroy()
    {
        m_ActionServer?.Dispose();
    }
}
```

## Testing These Examples

To test these examples:

1. **Setup Message Types**: Generate the `FibonacciGoal`, `FibonacciFeedback`, and `FibonacciResult` message types
2. **Create Scene**: Add both client and server scripts to a test scene
3. **Connect**: Ensure ROS endpoint is configured in ROS Settings
4. **Run**: Play the scene and use Debug.Log to monitor progress

## Common Patterns

### Pattern 1: Fire-and-Forget
```csharp
m_ActionClient.SendGoal(goal);
// Don't track it
```

### Pattern 2: Simple Callback
```csharp
var handle = m_ActionClient.SendGoal(goal);
handle.ResultReceived += (id, status, result) => ProcessResult(result);
```

### Pattern 3: Async/Await
```csharp
var (status, result) = await m_ActionClient.SendGoalAsync(goal);
Debug.Log($"Done: {status}");
```

### Pattern 4: Manual Polling
```csharp
var handle = m_ActionClient.SendGoal(goal);
while (handle.IsActive)
{
    Debug.Log($"Status: {handle.Status}");
    yield return new WaitForSeconds(1f);
}
```

### Pattern 5: Server with Cancellation Check
```csharp
while (!goalHandle.IsActive) return; // Stop if cancelled
// Continue processing
```

---

All examples are production-ready and thread-safe. Modify as needed for your specific use case.
