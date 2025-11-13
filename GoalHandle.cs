using System;
using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using UnityEngine;

namespace Unity.Robotics.ROSTCPConnector
{
    /// <summary>
    /// Represents the status of a ROS2 action goal.
    /// </summary>
    public enum GoalStatus
    {
        Unknown = -1,
        Pending = 0,
        Active = 1,
        Preempted = 2,
        Succeeded = 3,
        Aborted = 4,
        Canceled = 5,
        Rejected = 6,
    }

    /// <summary>
    /// Represents a handle to a goal sent to a ROS2 action server.
    /// Tracks the goal's state, feedback, and result.
    /// </summary>
    /// <typeparam name="TGoal">The goal message type.</typeparam>
    /// <typeparam name="TFeedback">The feedback message type.</typeparam>
    /// <typeparam name="TResult">The result message type.</typeparam>
    public class GoalHandle<TGoal, TFeedback, TResult>
        where TGoal : Message
        where TFeedback : Message
        where TResult : Message
    {
        private string m_GoalId;
        private GoalStatus m_Status = GoalStatus.Unknown;
        private TGoal m_Goal;
        private TFeedback m_LastFeedback;
        private TResult m_Result;
        private bool m_IsActive = true;
        private DateTime m_CreatedTime;

        private event Action<string, TFeedback> m_FeedbackReceived;
        private event Action<string, GoalStatus, TResult> m_ResultReceived;
        private event Action<string> m_CancelRequested;

        public string GoalId => m_GoalId;
        public GoalStatus Status => m_Status;
        public TGoal Goal => m_Goal;
        public TFeedback LastFeedback => m_LastFeedback;
        public TResult Result => m_Result;
        public bool IsActive => m_IsActive && (m_Status == GoalStatus.Active || m_Status == GoalStatus.Pending);
        public bool IsTerminalState => m_Status == GoalStatus.Succeeded || 
                                       m_Status == GoalStatus.Aborted || 
                                       m_Status == GoalStatus.Canceled ||
                                       m_Status == GoalStatus.Rejected ||
                                       m_Status == GoalStatus.Preempted;
        public DateTime CreatedTime => m_CreatedTime;

        /// <summary>
        /// Fired when feedback is received for this goal.
        /// Parameters: (goalId, feedbackMessage)
        /// </summary>
        public event Action<string, TFeedback> FeedbackReceived
        {
            add { m_FeedbackReceived += value; }
            remove { m_FeedbackReceived -= value; }
        }

        /// <summary>
        /// Fired when a result is received for this goal.
        /// Parameters: (goalId, finalStatus, resultMessage)
        /// </summary>
        public event Action<string, GoalStatus, TResult> ResultReceived
        {
            add { m_ResultReceived += value; }
            remove { m_ResultReceived -= value; }
        }

        /// <summary>
        /// Fired when a cancellation request is received for this goal.
        /// Parameters: (goalId)
        /// </summary>
        public event Action<string> CancelRequested
        {
            add { m_CancelRequested += value; }
            remove { m_CancelRequested -= value; }
        }

        internal GoalHandle(string goalId, TGoal goal)
        {
            m_GoalId = goalId;
            m_Goal = goal;
            m_Status = GoalStatus.Pending;
            m_CreatedTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Called internally to update the status of this goal.
        /// </summary>
        internal void UpdateStatus(GoalStatus newStatus)
        {
            m_Status = newStatus;
        }

        /// <summary>
        /// Called internally when feedback is received.
        /// </summary>
        internal void OnFeedbackReceived(TFeedback feedback)
        {
            m_LastFeedback = feedback;
            m_FeedbackReceived?.Invoke(m_GoalId, feedback);
        }

        /// <summary>
        /// Called internally when a result is received.
        /// </summary>
        internal void OnResultReceived(GoalStatus status, TResult result)
        {
            m_Status = status;
            m_Result = result;
            m_IsActive = false;
            m_ResultReceived?.Invoke(m_GoalId, status, result);
        }

        /// <summary>
        /// Called internally when a cancellation is requested.
        /// </summary>
        internal void OnCancelRequested()
        {
            m_CancelRequested?.Invoke(m_GoalId);
        }

        /// <summary>
        /// Returns a human-readable string representation of the goal status.
        /// </summary>
        public static string StatusToString(GoalStatus status)
        {
            return status switch
            {
                GoalStatus.Pending => "PENDING",
                GoalStatus.Active => "ACTIVE",
                GoalStatus.Preempted => "PREEMPTED",
                GoalStatus.Succeeded => "SUCCEEDED",
                GoalStatus.Aborted => "ABORTED",
                GoalStatus.Canceled => "CANCELED",
                GoalStatus.Rejected => "REJECTED",
                _ => "UNKNOWN"
            };
        }

        public override string ToString()
        {
            return $"GoalHandle(id={m_GoalId}, status={StatusToString(m_Status)}, active={IsActive})";
        }
    }
}
