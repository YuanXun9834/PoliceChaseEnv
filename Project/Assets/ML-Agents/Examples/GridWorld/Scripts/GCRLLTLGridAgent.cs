using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.SideChannels;
using System.Collections.Generic;
using System.Linq;
using System;

public class GCRLLTLGridAgent : Agent
{
    [Header("Environment")]
    public GridArea area;
    public float timeBetweenDecisionsAtInference;
    public Camera renderCamera;
    
    [Header("Agent Visuals")]
    public GameObject GreenBottom;
    public GameObject RedBottom;
    public GameObject YellowBottom;
    
    [Header("Settings")]
    public bool maskActions = true;
    [SerializeField] private int maxSteps = 1000;
    
    // Constants
    private const float AGENT_SCALE = 0.3f;
    private const float GOAL_REACH_THRESHOLD = 0.4f;
    
    // Action constants
    private const int k_NoAction = 0;
    private const int k_Up = 1;
    private const int k_Down = 2;
    private const int k_Left = 3;
    private const int k_Right = 4;

    // Components
    private LTLGoalSideChannel ltlGoalChannel;
    private VectorSensorComponent stateSensor;
    private EnvironmentParameters m_ResetParams;
    
    // State tracking
    private string currentLTLGoal;
    private bool episodeEnded;
    private float episodeReward;
    private float m_TimeSinceDecision;
    private float currentMoveSpeed = 1.0f;
    private bool hasGreenPowerup;
    private const float powerupSpeedMultiplier = 1.5f;

    // Goal sequence tracking
    private Queue<GridGoal> targetGoalSequence;
    private List<GridGoal> achievedGoals;
    private GridGoal? currentTargetGoal;
    private int currentStepCount;

    public enum GridGoal
    {
        GreenPlus,
        RedEx,
        YellowStar
    }

    private GridGoal m_CurrentGoal;
    public GridGoal CurrentGoal
    {
        get { return m_CurrentGoal; }
        set
        {
            m_CurrentGoal = value;
            GreenBottom.SetActive(value == GridGoal.GreenPlus);
            RedBottom.SetActive(value == GridGoal.RedEx);
            YellowBottom.SetActive(value == GridGoal.YellowStar);
        }
    }


    public override void Initialize()
    {
        base.Initialize();

        if (Academy.Instance.IsCommunicatorOn)
        {
            ltlGoalChannel = new LTLGoalSideChannel();
            SideChannelManager.RegisterSideChannel(ltlGoalChannel);
        }

        m_ResetParams = Academy.Instance.EnvironmentParameters;
        transform.localScale = Vector3.one * AGENT_SCALE;

        // Hide all bottom indicators initially
        GreenBottom.SetActive(false);
        RedBottom.SetActive(false);
        YellowBottom.SetActive(false);

        // Initialize the achievedGoals list
        achievedGoals = new List<GridGoal>();

        ResetState();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Agent position (2)
        sensor.AddObservation(transform.localPosition.x);
        sensor.AddObservation(transform.localPosition.z);
        
        // Current goal one-hot (3)
        sensor.AddObservation(currentTargetGoal.HasValue && currentTargetGoal.Value == GridGoal.GreenPlus ? 1f : 0f);
        sensor.AddObservation(currentTargetGoal.HasValue && currentTargetGoal.Value == GridGoal.RedEx ? 1f : 0f);
        sensor.AddObservation(currentTargetGoal.HasValue && currentTargetGoal.Value == GridGoal.YellowStar ? 1f : 0f);
        
        // Powerup status (1)
        sensor.AddObservation(hasGreenPowerup ? 1f : 0f);
        
        // Achieved goals (3)
        sensor.AddObservation(achievedGoals.Contains(GridGoal.GreenPlus) ? 1f : 0f);
        sensor.AddObservation(achievedGoals.Contains(GridGoal.RedEx) ? 1f : 0f);
        sensor.AddObservation(achievedGoals.Contains(GridGoal.YellowStar) ? 1f : 0f);
        
        // Goal positions (6 = 3 goals × 2 coordinates)
        if (area != null && area.actorObjs != null && area.actorObjs.Count > 0)
        {
            foreach (var goal in area.actorObjs)
            {
                if (goal != null)
                {
                    sensor.AddObservation(goal.transform.localPosition.x);
                    sensor.AddObservation(goal.transform.localPosition.z);
                }
            }
        }
        else
        {
            // Add zero values for missing goals
            for (int i = 0; i < 6; i++)
            {
                sensor.AddObservation(0f);
            }
        }
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        if (!maskActions) return;

        var positionX = (int)transform.localPosition.x;
        var positionZ = (int)transform.localPosition.z;
        var maxPosition = (int)m_ResetParams.GetWithDefault("gridSize", 5f) - 1;

        if (positionX == 0)
            actionMask.SetActionEnabled(0, k_Left, false);
        if (positionX == maxPosition)
            actionMask.SetActionEnabled(0, k_Right, false);
        if (positionZ == 0)
            actionMask.SetActionEnabled(0, k_Down, false);
        if (positionZ == maxPosition)
            actionMask.SetActionEnabled(0, k_Up, false);
    }

    public override void OnActionReceived(ActionBuffers actionBuffers)
    {
        if (episodeEnded) return;

        currentStepCount++;
        if (currentStepCount >= maxSteps)
        {
            SetReward(-1f);
            EndEpisode();
            return;
        }

        float beforeActionReward = GetCumulativeReward();
        
        AddReward(-0.01f);  // Small negative reward for each action
        var action = actionBuffers.DiscreteActions[0];
        
        var moveDistance = hasGreenPowerup ? currentMoveSpeed * powerupSpeedMultiplier : currentMoveSpeed;
        var targetPos = transform.position;
        switch (action)
        {
            case k_Right: targetPos += new Vector3(moveDistance, 0, 0f); break;
            case k_Left: targetPos += new Vector3(-moveDistance, 0, 0f); break;
            case k_Up: targetPos += new Vector3(0f, 0, moveDistance); break;
            case k_Down: targetPos += new Vector3(0f, 0, -moveDistance); break;
        }

        var hit = Physics.OverlapBox(targetPos, new Vector3(0.3f, 0.3f, 0.3f));
        if (hit.Where(col => col.gameObject.CompareTag("wall")).ToArray().Length == 0)
        {
            transform.position = targetPos;
            CheckGoalAchievement();
        }

        float afterActionReward = GetCumulativeReward();
        episodeReward += (afterActionReward - beforeActionReward);

        if (ltlGoalChannel != null && ltlGoalChannel.CurrentLTLGoal != currentLTLGoal)
        {
            currentLTLGoal = ltlGoalChannel.CurrentLTLGoal;
            UpdateGoalBasedOnLTL();
        }
    }

    private void CheckGoalAchievement()
    {
        if (targetGoalSequence == null || targetGoalSequence.Count == 0 || !currentTargetGoal.HasValue) return;

        var nearbyObjects = Physics.OverlapSphere(transform.position, GOAL_REACH_THRESHOLD);
        
        // Check for green powerup
        if (!hasGreenPowerup && nearbyObjects.Any(col => col.gameObject.CompareTag("plus")))
        {
            hasGreenPowerup = true;
            AddReward(2.0f);
        }

        bool goalReached = false;
        switch (currentTargetGoal.Value)  // Use .Value to access the non-nullable value
        {
            case GridGoal.GreenPlus:
                goalReached = nearbyObjects.Any(col => col.gameObject.CompareTag("plus"));
                break;
            case GridGoal.RedEx:
                goalReached = nearbyObjects.Any(col => col.gameObject.CompareTag("ex"));
                break;
            case GridGoal.YellowStar:
                goalReached = nearbyObjects.Any(col => col.gameObject.CompareTag("star"));
                break;
        }

        if (goalReached)
        {
            achievedGoals.Add(currentTargetGoal.Value);
            float reward = hasGreenPowerup ? 3f : 1f;
            AddReward(reward);

            targetGoalSequence.Dequeue();
            
            if (targetGoalSequence.Count > 0)
            {
                currentTargetGoal = targetGoalSequence.Peek();
                if (currentTargetGoal.HasValue)
                {
                    CurrentGoal = currentTargetGoal.Value;
                }
                Debug.Log($"Goal achieved! Moving to next goal: {currentTargetGoal}");
            }
            else
            {
                AddReward(5f);
                Debug.Log("All goals achieved in correct sequence!");
                EndEpisode();
            }
        }
    }

    public override void OnEpisodeBegin()
    {
        area.AreaReset();
        ResetState();
        
        if (targetGoalSequence != null && targetGoalSequence.Count > 0)
        {
            currentTargetGoal = targetGoalSequence.Peek();
            if (currentTargetGoal.HasValue)
            {
                CurrentGoal = currentTargetGoal.Value;
            }
        }
        else
        {
            currentTargetGoal = null;
            GreenBottom.SetActive(false);
            RedBottom.SetActive(false);
            YellowBottom.SetActive(false);
        }
    }

    private void ResetState()
    {
        episodeEnded = false;
        episodeReward = 0f;
        hasGreenPowerup = false;
        currentMoveSpeed = 1.0f;
        m_TimeSinceDecision = 0f;
        currentStepCount = 0;
        if (achievedGoals != null) achievedGoals.Clear();
        currentTargetGoal = null;
    }

    public void SetTargetSequence(IEnumerable<GridGoal> sequence)
    {
        if (sequence == null) return;
        
        targetGoalSequence = new Queue<GridGoal>(sequence);
        if (targetGoalSequence.Count > 0)
        {
            currentTargetGoal = targetGoalSequence.Peek();
            CurrentGoal = currentTargetGoal.Value;
            Debug.Log($"Set new target sequence with {targetGoalSequence.Count} goals");
        }
    }

    private void UpdateGoalBasedOnLTL()
    {
        if (string.IsNullOrEmpty(currentLTLGoal)) return;

        if (currentLTLGoal.Contains("green"))
            CurrentGoal = GridGoal.GreenPlus;
        else if (currentLTLGoal.Contains("red"))
            CurrentGoal = GridGoal.RedEx;
        else if (currentLTLGoal.Contains("yellow"))
            CurrentGoal = GridGoal.YellowStar;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discreteActionsOut = actionsOut.DiscreteActions;
        discreteActionsOut[0] = k_NoAction;
        
        if (Input.GetKey(KeyCode.D)) discreteActionsOut[0] = k_Right;
        if (Input.GetKey(KeyCode.W)) discreteActionsOut[0] = k_Up;
        if (Input.GetKey(KeyCode.A)) discreteActionsOut[0] = k_Left;
        if (Input.GetKey(KeyCode.S)) discreteActionsOut[0] = k_Down;
    }

    private void FixedUpdate()
    {
        if (renderCamera != null)
            renderCamera.Render();

        if (Academy.Instance.IsCommunicatorOn)
        {
            RequestDecision();
        }
        else if (m_TimeSinceDecision >= timeBetweenDecisionsAtInference)
        {
            m_TimeSinceDecision = 0f;
            RequestDecision();
        }
        else
        {
            m_TimeSinceDecision += Time.fixedDeltaTime;
        }
    }

    private void OnDestroy()
    {
        if (ltlGoalChannel != null)
            SideChannelManager.UnregisterSideChannel(ltlGoalChannel);
    }
}