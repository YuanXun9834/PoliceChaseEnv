using System;
using Unity.MLAgents.SideChannels;
using UnityEngine;

public class LTLGoalSideChannel : SideChannel
{
    public LTLGoalSideChannel()
    {
        ChannelId = new Guid("621f0a70-4f87-11ea-a6bf-784f4387d1f7");
    }

    public string CurrentLTLGoal { get; private set; }

    protected override void OnMessageReceived(IncomingMessage msg)
    {
        CurrentLTLGoal = msg.ReadString();
        Debug.Log($"Received new LTL goal: {CurrentLTLGoal}");
    }

    public void SendLTLGoalToTrainer(string goal)
    {
        using (var msg = new OutgoingMessage())
        {
            msg.WriteString(goal);
            QueueMessageToSend(msg);
        }
    }
}