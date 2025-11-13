using System;
using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using UnityEngine;

namespace Unity.Robotics.ROSTCPConnector
{
    /// <summary>
    /// Lightweight convenience wrapper for implementing ROS2 action servers in Unity.
    /// </summary>
    /// <typeparam name="TGoal">Action goal message type.</typeparam>
    /// <typeparam name="TFeedback">Action feedback message type.</typeparam>
    /// <typeparam name="TResult">Action result message type.</typeparam>
    public class RosActionServer<TGoal, TFeedback, TResult> : IDisposable
        where TGoal : Message
        where TFeedback : Message
        where TResult : Message
    {
        readonly ROSConnection m_Connection;
        readonly string m_ActionName;
        readonly string m_ActionType;
        bool m_Disposed;

        // Track active goals being processed
        readonly Dictionary<string, GoalHandle<TGoal, TFeedback, TResult>> m_GoalHandles =
            new Dictionary<string, GoalHandle<TGoal, TFeedback, TResult>>();
        readonly object m_GoalHandlesLock = new object();

        readonly MessageDeserializer m_GoalDeserializer = new MessageDeserializer();
        readonly object m_GoalDeserializerGate = new object();

        // Callback when a new goal is received
        Func<string, TGoal, GoalHandle<TGoal, TFeedback, TResult>> m_OnGoalReceived;
        Action<string> m_OnCancelRequested;

        public RosActionServer(ROSConnection connection, string actionName, string actionType)
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
        }

        public string ActionName => m_ActionName;
        public string ActionType => m_ActionType;

        /// <summary>
        /// Registers this action server with callbacks for goal and cancellation requests.
        /// </summary>
        public void RegisterServer(
            Func<string, TGoal, GoalHandle<TGoal, TFeedback, TResult>> onGoalReceived,
            Action<string> onCancelRequested = null)
        {
            if (onGoalReceived == null)
                throw new ArgumentNullException(nameof(onGoalReceived));

            m_OnGoalReceived = onGoalReceived;
            m_OnCancelRequested = onCancelRequested;

            // Wrap the goal handler to deserialize the goal
            Func<string, byte[], GoalHandle<TGoal, TFeedback, TResult>> wrappedHandler =
                (goalId, goalBytes) =>
                {
                    try
                    {
                        var goal = Deserialize<TGoal>(goalBytes, m_GoalDeserializer, m_GoalDeserializerGate);
                        var handle = m_OnGoalReceived(goalId, goal);

                        if (handle != null)
                        {
                            lock (m_GoalHandlesLock)
                            {
                                m_GoalHandles[goalId] = handle;
                            }
                        }

                        return handle;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                        return null;
                    }
                };

            m_Connection.ImplementRosActionServer(
                m_ActionName,
                wrappedHandler,
                m_OnCancelRequested);
        }

        /// <summary>
        /// Sends feedback for an active goal.
        /// </summary>
        public void PublishFeedback(string goalId, TFeedback feedback)
        {
            if (string.IsNullOrEmpty(goalId))
                throw new ArgumentException("goalId cannot be null or empty.", nameof(goalId));
            if (feedback == null)
                throw new ArgumentNullException(nameof(feedback));

            ThrowIfDisposed();
            m_Connection.SendActionFeedback(m_ActionName, goalId, feedback);
        }

        /// <summary>
        /// Marks a goal as succeeded and sends the result.
        /// </summary>
        public void Succeed(string goalId, TResult result)
        {
            if (string.IsNullOrEmpty(goalId))
                throw new ArgumentException("goalId cannot be null or empty.", nameof(goalId));
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            ThrowIfDisposed();
            m_Connection.SendActionResult(m_ActionName, goalId, GoalStatus.Succeeded, result);
            RemoveGoalHandle(goalId);
        }

        /// <summary>
        /// Marks a goal as aborted and sends the result.
        /// </summary>
        public void Abort(string goalId, TResult result)
        {
            if (string.IsNullOrEmpty(goalId))
                throw new ArgumentException("goalId cannot be null or empty.", nameof(goalId));
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            ThrowIfDisposed();
            m_Connection.SendActionResult(m_ActionName, goalId, GoalStatus.Aborted, result);
            RemoveGoalHandle(goalId);
        }

        /// <summary>
        /// Marks a goal as canceled and sends the result.
        /// </summary>
        public void Cancel(string goalId, TResult result)
        {
            if (string.IsNullOrEmpty(goalId))
                throw new ArgumentException("goalId cannot be null or empty.", nameof(goalId));
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            ThrowIfDisposed();
            m_Connection.SendActionResult(m_ActionName, goalId, GoalStatus.Canceled, result);
            RemoveGoalHandle(goalId);
        }

        /// <summary>
        /// Updates the status of an active goal.
        /// </summary>
        public void UpdateGoalStatus(string goalId, GoalStatus status)
        {
            if (string.IsNullOrEmpty(goalId))
                throw new ArgumentException("goalId cannot be null or empty.", nameof(goalId));

            ThrowIfDisposed();
            m_Connection.UpdateActionGoalStatus(m_ActionName, goalId, status);
        }

        /// <summary>
        /// Gets a goal handle by its ID.
        /// </summary>
        public GoalHandle<TGoal, TFeedback, TResult> GetGoal(string goalId)
        {
            if (string.IsNullOrEmpty(goalId))
                return null;

            lock (m_GoalHandlesLock)
            {
                m_GoalHandles.TryGetValue(goalId, out var handle);
                return handle;
            }
        }

        /// <summary>
        /// Gets all currently active goal handles.
        /// </summary>
        public IEnumerable<GoalHandle<TGoal, TFeedback, TResult>> GetActiveGoals()
        {
            lock (m_GoalHandlesLock)
            {
                return new List<GoalHandle<TGoal, TFeedback, TResult>>(m_GoalHandles.Values);
            }
        }

        void RemoveGoalHandle(string goalId)
        {
            lock (m_GoalHandlesLock)
            {
                m_GoalHandles.Remove(goalId);
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

            lock (m_GoalHandlesLock)
            {
                m_GoalHandles.Clear();
            }
        }

        void ThrowIfDisposed()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(RosActionServer<TGoal, TFeedback, TResult>));
        }
    }
}
