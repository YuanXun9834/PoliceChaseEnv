using System;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.SideChannels;
using System.Collections.Generic;

public class GCRLLTLGridAgent : GridAgent
{
    [Header("GCRLLTL Specific")]
    private LTLGoalSideChannel ltlGoalChannel;
    private string currentLTLGoal;
    private Dictionary<string, float[]> zoneVectors;
    private const int ZONE_VECTOR_SIZE = 24;
    
    private bool episodeEnded = false;

    public override void Initialize()
    {
        base.Initialize();
        
        // Initialize LTL side channel
        ltlGoalChannel = new LTLGoalSideChannel();
        SideChannelManager.RegisterSideChannel(ltlGoalChannel);
        
        // Initialize zone vectors
        InitializeZoneVectors();
    }

    private void InitializeZoneVectors()
    {
        // Initialize dictionary to store zone vectors
        zoneVectors = new Dictionary<string, float[]>();
        
        // Example zone vectors - modify based on your needs
        zoneVectors["green"] = new float[ZONE_VECTOR_SIZE];  // Green Plus zone
        zoneVectors["red"] = new float[ZONE_VECTOR_SIZE];    // Red Ex zone
        zoneVectors["yellow"] = new float[ZONE_VECTOR_SIZE]; // Yellow Star zone
        
        // Set specific values for each zone vector
        for (int i = 0; i < ZONE_VECTOR_SIZE; i++)
        {
            if (i < 8) zoneVectors["green"][i] = 1f;
            else if (i < 16) zoneVectors["red"][i] = 1f;
            else zoneVectors["yellow"][i] = 1f;
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Add base observations (position, etc.)
        base.CollectObservations(sensor);
        
        // Add agent's current position
        sensor.AddObservation(transform.localPosition.x);
        sensor.AddObservation(transform.localPosition.z);
        
        // Add observations for each goal
        foreach (var goal in area.actorObjs)
        {
            sensor.AddObservation(goal.transform.localPosition.x);
            sensor.AddObservation(goal.transform.localPosition.z);
            
            // Add goal type
            if (goal.CompareTag("plus")) sensor.AddObservation(1f);
            else if (goal.CompareTag("ex")) sensor.AddObservation(2f);
            else if (goal.CompareTag("star")) sensor.AddObservation(3f);
        }
        
        // Add current goal vector based on LTL specification
        float[] currentGoalVector = GetCurrentGoalVector();
        if (currentGoalVector != null)
        {
            sensor.AddObservation(currentGoalVector);
        }
    }

    private float[] GetCurrentGoalVector()
    {
        // Parse current LTL goal and return appropriate zone vector
        if (string.IsNullOrEmpty(currentLTLGoal)) return null;
        
        if (currentLTLGoal.Contains("green")) return zoneVectors["green"];
        if (currentLTLGoal.Contains("red")) return zoneVectors["red"];
        if (currentLTLGoal.Contains("yellow")) return zoneVectors["yellow"];
        
        return null;
    }

    public override void OnActionReceived(ActionBuffers actionBuffers)
    {
        base.OnActionReceived(actionBuffers);

        // Check if LTL goal has been updated
        if (ltlGoalChannel.CurrentLTLGoal != currentLTLGoal)
        {
            currentLTLGoal = ltlGoalChannel.CurrentLTLGoal;
            UpdateGoalBasedOnLTL();
        }

        // Handle episode ending
        if (episodeEnded)
        {
            base.EndEpisode();
            episodeEnded = false;
        }
    }

    private void UpdateGoalBasedOnLTL()
    {
        if (string.IsNullOrEmpty(currentLTLGoal)) return;

        // Parse LTL formula and update current goal
        if (currentLTLGoal.Contains("green"))
        {
            CurrentGoal = GridGoal.GreenPlus;
        }
        else if (currentLTLGoal.Contains("red"))
        {
            CurrentGoal = GridGoal.RedEx;
        }
        else if (currentLTLGoal.Contains("yellow"))
        {
            CurrentGoal = GridGoal.YellowStar;
        }
    }

    public override void OnEpisodeBegin()
    {
        base.OnEpisodeBegin();
        episodeEnded = false;
    }

    // Instead of overriding EndEpisode, we'll set a flag and handle it in OnActionReceived
    public void TriggerEpisodeEnd()
    {
        episodeEnded = true;
    }

    public Dictionary<string, object> GetStateInfo()
    {
        return new Dictionary<string, object>
        {
            {"position", transform.position},
            {"current_goal", CurrentGoal},
            {"ltl_goal", currentLTLGoal},
            {"goal_vector", GetCurrentGoalVector()}
        };
    }

    private void OnDestroy()
    {
        if (ltlGoalChannel != null)
        {
            SideChannelManager.UnregisterSideChannel(ltlGoalChannel);
        }
    }
}