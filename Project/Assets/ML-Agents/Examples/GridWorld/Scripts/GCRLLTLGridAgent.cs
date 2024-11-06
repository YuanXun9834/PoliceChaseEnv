using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.SideChannels;
using System.Collections.Generic;
using System;

public class GCRLLTLGridAgent : GridAgent
{
    [Header("GCRLLTL Specific")]
    private LTLGoalSideChannel ltlGoalChannel;
    private string currentLTLGoal;
    private Dictionary<string, float[]> zoneVectors;
    private VectorSensorComponent stateSensor;
    private VectorSensorComponent goalSensor;
    private const int ZONE_VECTOR_SIZE = 24;
    private bool episodeEnded;
    private float episodeReward;
    private float[] currentGoalVector;

    public override void Initialize()
    {
        base.Initialize();
        
        ltlGoalChannel = new LTLGoalSideChannel();
        SideChannelManager.RegisterSideChannel(ltlGoalChannel);
        
        // Get references to sensors
        var sensorComponents = GetComponents<VectorSensorComponent>();
        foreach (var component in sensorComponents)
        {
            if (component.SensorName == "StateVector")
                stateSensor = component;
            else if (component.SensorName == "GoalVector")
                goalSensor = component;
        }
        
        InitializeZoneVectors();
        currentGoalVector = new float[ZONE_VECTOR_SIZE];
        episodeEnded = false;
        episodeReward = 0f;
        
        UpdateGoalVector();
    }

    private void InitializeZoneVectors()
    {
        zoneVectors = new Dictionary<string, float[]>
        {
            {"green", new float[ZONE_VECTOR_SIZE]},
            {"red", new float[ZONE_VECTOR_SIZE]},
            {"yellow", new float[ZONE_VECTOR_SIZE]}
        };

        // Initialize zone vectors
        for (int i = 0; i < 8; i++)
        {
            int baseIdx = i * 3;
            // Green plus
            zoneVectors["green"][baseIdx] = 1f;
            // Red ex
            zoneVectors["red"][baseIdx + 1] = 1f;
            // Yellow star
            zoneVectors["yellow"][baseIdx + 2] = 1f;
        }
    }

    private void UpdateGoalVector()
    {
        Array.Clear(currentGoalVector, 0, ZONE_VECTOR_SIZE);
        
        string goalType = CurrentGoal switch
        {
            GridGoal.GreenPlus => "green",
            GridGoal.RedEx => "red",
            GridGoal.YellowStar => "yellow",
            _ => null
        };

        if (goalType != null && zoneVectors.ContainsKey(goalType))
        {
            Array.Copy(zoneVectors[goalType], currentGoalVector, ZONE_VECTOR_SIZE);
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (stateSensor != null)
            CollectStateObservations(stateSensor.GetSensor());
        
        if (goalSensor != null)
            CollectGoalObservations(goalSensor.GetSensor());
    }

    private void CollectStateObservations(VectorSensor sensor)
    {
        // Agent position
        sensor.AddObservation(transform.localPosition.x);
        sensor.AddObservation(transform.localPosition.z);
        
        // Current goal one-hot encoding
        sensor.AddObservation(CurrentGoal == GridGoal.GreenPlus ? 1f : 0f);
        sensor.AddObservation(CurrentGoal == GridGoal.RedEx ? 1f : 0f);
        sensor.AddObservation(CurrentGoal == GridGoal.YellowStar ? 1f : 0f);
        
        // Goals' positions and types
        if (area != null && area.actorObjs != null)
        {
            foreach (var goal in area.actorObjs)
            {
                if (goal != null)
                {
                    sensor.AddObservation(goal.transform.localPosition.x);
                    sensor.AddObservation(goal.transform.localPosition.z);
                    
                    float goalType = goal.CompareTag("plus") ? 1f : 
                                   goal.CompareTag("ex") ? 2f : 
                                   goal.CompareTag("star") ? 3f : 0f;
                    sensor.AddObservation(goalType);
                }
            }
        }

        // Pad observations if needed
        int currentObs = 5 + (area?.actorObjs?.Count * 3 ?? 0);
        for (int i = currentObs; i < 14; i++)
        {
            sensor.AddObservation(0f);
        }
    }

    private void CollectGoalObservations(VectorSensor sensor)
    {
        // Add current goal vector
        sensor.AddObservation(currentGoalVector);
    }

    public override void OnActionReceived(ActionBuffers actionBuffers)
    {
        if (episodeEnded)
            return;

        float beforeActionReward = GetCumulativeReward();
        
        base.OnActionReceived(actionBuffers);
        
        float afterActionReward = GetCumulativeReward();
        episodeReward += (afterActionReward - beforeActionReward);

        // Check LTL goal updates
        if (ltlGoalChannel.CurrentLTLGoal != currentLTLGoal)
        {
            currentLTLGoal = ltlGoalChannel.CurrentLTLGoal;
            UpdateGoalBasedOnLTL();
        }

        // Add info about goal achievement
        var info = new Dictionary<string, object>
        {
            {"goal_achieved", HasReachedGoal()},
            {"current_goal", CurrentGoal.ToString()}
        };
    }

    private bool HasReachedGoal()
    {
        // Implement based on your goal achievement criteria
        // This should match your reward logic
        return false; // Replace with actual implementation
    }

    private void UpdateGoalBasedOnLTL()
    {
        if (string.IsNullOrEmpty(currentLTLGoal))
            return;

        if (currentLTLGoal.Contains("green"))
            CurrentGoal = GridGoal.GreenPlus;
        else if (currentLTLGoal.Contains("red"))
            CurrentGoal = GridGoal.RedEx;
        else if (currentLTLGoal.Contains("yellow"))
            CurrentGoal = GridGoal.YellowStar;

        UpdateGoalVector();
    }

    public override void OnEpisodeBegin()
    {
        base.OnEpisodeBegin();
        episodeEnded = false;
        episodeReward = 0f;
        UpdateGoalVector();
    }

    private void OnDestroy()
    {
        if (ltlGoalChannel != null)
            SideChannelManager.UnregisterSideChannel(ltlGoalChannel);
    }
}