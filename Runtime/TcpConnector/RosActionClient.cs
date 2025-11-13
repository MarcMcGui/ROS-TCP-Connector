using System;
using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using UnityEngine;

namespace Unity.Robotics.ROSTCPConnector
{
    /// <summary>
    /// Lightweight convenience wrapper for driving a ROS action client from Unity.
    /// </summary>
    /// <typeparam name="TGoal">Action goal message type.</typeparam>
    /// <typeparam name="TFeedback">Action feedback message type.</typeparam>
    /// <typeparam name="TResult">Action result message type.</typeparam>
    public class RosActionClient<TGoal, TFeedback, TResult> : IDisposable
        where TGoal : Message
        where TFeedback : Message
        where TResult : Message
    {
        readonly ROSConnection m_Connection;
        readonly string m_ActionName;
        readonly string m_ActionType;
        readonly IDisposable m_ActionListenerHandle;
        bool m_Disposed;

        readonly MessageDeserializer m_FeedbackDeserializer = new MessageDeserializer();
        readonly MessageDeserializer m_ResultDeserializer = new MessageDeserializer();
        readonly object m_FeedbackDeserializerGate = new object();
        readonly object m_ResultDeserializerGate = new object();

        // Track active goals
        readonly Dictionary<string, GoalHandle<TGoal, TFeedback, TResult>> m_ActiveGoals =
            new Dictionary<string, GoalHandle<TGoal, TFeedback, TResult>>();
        readonly object m_ActiveGoalsLock = new object();

        public RosActionClient(ROSConnection connection, string actionName, string actionType)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            if (string.IsNullOrEmpty(actionName))
                throw new ArgumentException("actionName cannot be null or empty.", nameof(actionName));
            if (string.IsNullOrEmpty(actionType))
                throw new ArgumentException("actionType cannot be null or empty.", nameof(actionType));

            m_Connection = connection;
            m_ActionName = actionName;
            m_ActionType = actionType;

            // Ensure the ROS-TCP-Endpoint is aware of this action client before goals are sent.
            m_Connection.RegisterRosActionClient(m_ActionName, m_ActionType);
            m_ActionListenerHandle = m_Connection.RegisterActionHandlers(
                m_ActionName,
                HandleFeedbackBytes,
                HandleResultBytes);
        }

        public string ActionName => m_ActionName;
        public string ActionType => m_ActionType;

        [Obsolete("Use SendGoalAsync() or check GoalHandle.FeedbackReceived event instead")]
        public event Action<string, TFeedback> FeedbackReceived;
        
        [Obsolete("Use GoalHandle.ResultReceived event instead")]
        public event Action<string, TResult> ResultReceived;

        /// <summary>
        /// Sends a goal to the action server and returns a GoalHandle to track its progress.
        /// </summary>
        public GoalHandle<TGoal, TFeedback, TResult> SendGoal(TGoal goal, string goalId = null)
        {
            if (goal == null)
                throw new ArgumentNullException(nameof(goal));
            ThrowIfDisposed();

            string actualGoalId = m_Connection.SendActionGoal(m_ActionName, goal, goalId);
            var handle = new GoalHandle<TGoal, TFeedback, TResult>(actualGoalId, goal);

            lock (m_ActiveGoalsLock)
            {
                m_ActiveGoals[actualGoalId] = handle;
            }

            return handle;
        }

        /// <summary>
        /// Sends a goal asynchronously and waits for the result.
        /// </summary>
        public async System.Threading.Tasks.Task<(GoalStatus Status, TResult Result)> SendGoalAsync(
            TGoal goal,
            string goalId = null)
        {
            var handle = SendGoal(goal, goalId);
            
            return await System.Threading.Tasks.Task.Run(() =>
            {
                // Wait for terminal state with timeout
                var startTime = System.DateTime.UtcNow;
                var timeout = System.TimeSpan.FromSeconds(300); // 5 minute default timeout
                
                while (!handle.IsTerminalState)
                {
                    if (System.DateTime.UtcNow - startTime > timeout)
                    {
                        throw new System.TimeoutException(
                            $"Goal {handle.GoalId} did not complete within {timeout.TotalSeconds} seconds");
                    }
                    System.Threading.Thread.Sleep(100);
                }

                return (handle.Status, handle.Result);
            });
        }

        /// <summary>
        /// Gets a goal handle by its ID if it's still active.
        /// </summary>
        public GoalHandle<TGoal, TFeedback, TResult> GetGoal(string goalId)
        {
            if (string.IsNullOrEmpty(goalId))
                return null;

            lock (m_ActiveGoalsLock)
            {
                m_ActiveGoals.TryGetValue(goalId, out var handle);
                return handle;
            }
        }

        /// <summary>
        /// Checks if a goal is currently active.
        /// </summary>
        public bool IsGoalActive(string goalId)
        {
            if (string.IsNullOrEmpty(goalId))
                return false;

            lock (m_ActiveGoalsLock)
            {
                return m_ActiveGoals.TryGetValue(goalId, out var handle) && handle.IsActive;
            }
        }

        /// <summary>
        /// Gets the status of a goal.
        /// </summary>
        public GoalStatus? GetGoalStatus(string goalId)
        {
            var handle = GetGoal(goalId);
            return handle?.Status;
        }

        /// <summary>
        /// Gets all currently active goals.
        /// </summary>
        public IEnumerable<GoalHandle<TGoal, TFeedback, TResult>> GetActiveGoals()
        {
            lock (m_ActiveGoalsLock)
            {
                return new List<GoalHandle<TGoal, TFeedback, TResult>>(m_ActiveGoals.Values);
            }
        }

        /// <summary>
        /// Cancels a goal.
        /// </summary>
        public void CancelGoal(string goalId)
        {
            if (string.IsNullOrEmpty(goalId))
                throw new ArgumentException("goalId cannot be null or empty.", nameof(goalId));
            ThrowIfDisposed();
            m_Connection.CancelActionGoal(m_ActionName, goalId);
        }

        /// <summary>
        /// Cancels all active goals.
        /// </summary>
        public void CancelAllGoals()
        {
            ThrowIfDisposed();
            lock (m_ActiveGoalsLock)
            {
                foreach (var goalId in new List<string>(m_ActiveGoals.Keys))
                {
                    m_Connection.CancelActionGoal(m_ActionName, goalId);
                }
            }
        }

        void HandleFeedbackBytes(string goalId, byte[] payload)
        {
            var handle = GetGoal(goalId);
            
            try
            {
                var message = Deserialize<TFeedback>(payload, m_FeedbackDeserializer, m_FeedbackDeserializerGate);
                
                if (handle != null)
                {
                    handle.OnFeedbackReceived(message);
                }

                // Call legacy event for backwards compatibility
                FeedbackReceived?.Invoke(goalId, message);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        void HandleResultBytes(string goalId, byte[] payload)
        {
            var handle = GetGoal(goalId);
            
            try
            {
                var message = Deserialize<TResult>(payload, m_ResultDeserializer, m_ResultDeserializerGate);
                
                if (handle != null)
                {
                    // Default to Succeeded if status wasn't explicitly set
                    handle.OnResultReceived(GoalStatus.Succeeded, message);
                }

                // Call legacy event for backwards compatibility
                ResultReceived?.Invoke(goalId, message);

                // Clean up from active goals once result is received
                lock (m_ActiveGoalsLock)
                {
                    m_ActiveGoals.Remove(goalId);
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        static T Deserialize<T>(byte[] payload, MessageDeserializer deserializer, object gate) where T : Message
        {
            lock (gate)
            {
                return deserializer.DeserializeMessage<T>(payload);
            }
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;

            m_Disposed = true;
            
            lock (m_ActiveGoalsLock)
            {
                m_ActiveGoals.Clear();
            }

            m_ActionListenerHandle?.Dispose();
        }

        void ThrowIfDisposed()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(RosActionClient<TGoal, TFeedback, TResult>));
        }
    }
}
