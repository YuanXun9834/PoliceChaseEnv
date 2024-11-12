using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.SideChannels;
using System.Collections.Generic;
using System;
using System.Linq;

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
    [SerializeField] private float powerupSpeedMultiplier = 1.5f;
    
    // Constants
    private const int ZONE_VECTOR_SIZE = 24;
    private const int ACTION_SIZE = 5;
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
    private VectorSensorComponent goalSensor;
    
    // State tracking
    private string currentLTLGoal;
    private Dictionary<string, float[]> zoneVectors;
    private bool episodeEnded;
    private float episodeReward;
    private HashSet<string> visitedZones;
    private bool hasGreenPowerup;
    private float m_TimeSinceDecision;
    private EnvironmentParameters m_ResetParams;
    private float currentMoveSpeed = 1.0f;

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
            switch (value)
            {
                case GridGoal.GreenPlus:
                    GreenBottom.SetActive(true);
                    RedBottom.SetActive(false);
                    YellowBottom.SetActive(false);
                    break;
                case GridGoal.RedEx:
                    GreenBottom.SetActive(false);
                    RedBottom.SetActive(true);
                    YellowBottom.SetActive(false);
                    break;
                case GridGoal.YellowStar:
                    GreenBottom.SetActive(false);
                    RedBottom.SetActive(false);
                    YellowBottom.SetActive(true);
                    break;
            }
            m_CurrentGoal = value;
        }
    }

    public override void Initialize()
    {
        // Get sensors
        var sensors = GetComponents<VectorSensorComponent>();
        foreach (var sensor in sensors)
        {
            if (sensor.SensorName == "StateSensor")
                stateSensor = sensor;
            else if (sensor.SensorName == "GoalSensor")
                goalSensor = sensor;
        }

        // Initialize side channel if in training mode
        if (Academy.Instance.IsCommunicatorOn)
        {
            ltlGoalChannel = new LTLGoalSideChannel();
            SideChannelManager.RegisterSideChannel(ltlGoalChannel);
        }

        m_ResetParams = Academy.Instance.EnvironmentParameters;
        transform.localScale = Vector3.one * AGENT_SCALE;
        
        InitializeZoneVectors();
        ResetState();
    }

    private void InitializeZoneVectors()
    {
        zoneVectors = new Dictionary<string, float[]>();
        float[] greenVector = new float[ZONE_VECTOR_SIZE];
        float[] redVector = new float[ZONE_VECTOR_SIZE];
        float[] yellowVector = new float[ZONE_VECTOR_SIZE];

        for (int i = 0; i < 8; i++)
        {
            // Set RGB values for each zone type across 8 sets
            greenVector[i * 3] = 1f;
            redVector[i * 3 + 1] = 1f;
            yellowVector[i * 3 + 2] = 1f;
        }

        zoneVectors["green"] = NormalizeVector(greenVector);
        zoneVectors["red"] = NormalizeVector(redVector);
        zoneVectors["yellow"] = NormalizeVector(yellowVector);
    }

    private float[] NormalizeVector(float[] vector)
    {
        float magnitude = Mathf.Sqrt(vector.Sum(x => x * x));
        return magnitude > 0 ? vector.Select(x => x / magnitude).ToArray() : vector;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (stateSensor != null)
        {
            // Agent position (2)
            stateSensor.GetSensor().AddObservation(transform.localPosition.x);
            stateSensor.GetSensor().AddObservation(transform.localPosition.z);
            
            // Current goal one-hot encoding (3)
            stateSensor.GetSensor().AddObservation(CurrentGoal == GridGoal.GreenPlus ? 1f : 0f);
            stateSensor.GetSensor().AddObservation(CurrentGoal == GridGoal.RedEx ? 1f : 0f);
            stateSensor.GetSensor().AddObservation(CurrentGoal == GridGoal.YellowStar ? 1f : 0f);
            
            // Powerup status (1)
            stateSensor.GetSensor().AddObservation(hasGreenPowerup ? 1f : 0f);
            
            // Visited zones (3)
            stateSensor.GetSensor().AddObservation(visitedZones.Contains("green") ? 1f : 0f);
            stateSensor.GetSensor().AddObservation(visitedZones.Contains("red") ? 1f : 0f);
            stateSensor.GetSensor().AddObservation(visitedZones.Contains("yellow") ? 1f : 0f);
        }

        if (goalSensor != null)
        {
            string currentZone = GetCurrentZone();
            float[] zoneVector = !string.IsNullOrEmpty(currentZone) && zoneVectors.ContainsKey(currentZone) 
                ? zoneVectors[currentZone] 
                : new float[ZONE_VECTOR_SIZE];
                
            foreach (float value in zoneVector)
            {
                goalSensor.GetSensor().AddObservation(value);
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

        float beforeActionReward = GetCumulativeReward();
        
        AddReward(-0.01f);  // Small negative reward for each action
        var action = actionBuffers.DiscreteActions[0];
        
        // Calculate target position with speed multiplier
        var moveDistance = hasGreenPowerup ? currentMoveSpeed * powerupSpeedMultiplier : currentMoveSpeed;
        var targetPos = transform.position;
        switch (action)
        {
            case k_Right: targetPos += new Vector3(moveDistance, 0, 0f); break;
            case k_Left: targetPos += new Vector3(-moveDistance, 0, 0f); break;
            case k_Up: targetPos += new Vector3(0f, 0, moveDistance); break;
            case k_Down: targetPos += new Vector3(0f, 0, -moveDistance); break;
        }

        // Check if move is valid and execute
        var hit = Physics.OverlapBox(targetPos, new Vector3(0.3f, 0.3f, 0.3f));
        if (hit.Where(col => col.gameObject.CompareTag("wall")).ToArray().Length == 0)
        {
            transform.position = targetPos;
            CheckGoalAchievement();
        }

        float afterActionReward = GetCumulativeReward();
        episodeReward += (afterActionReward - beforeActionReward);

        // Update goal if LTL goal has changed
        if (ltlGoalChannel != null && ltlGoalChannel.CurrentLTLGoal != currentLTLGoal)
        {
            currentLTLGoal = ltlGoalChannel.CurrentLTLGoal;
            UpdateGoalBasedOnLTL();
        }
    }

    private void CheckGoalAchievement()
    {
        var nearbyObjects = Physics.OverlapSphere(transform.position, GOAL_REACH_THRESHOLD);
        
        // Check for green powerup first
        if (!hasGreenPowerup && nearbyObjects.Any(col => col.gameObject.CompareTag("plus")))
        {
            hasGreenPowerup = true;
            AddReward(2.0f); // Bonus for getting green first
        }

        // Check goal achievement
        if (nearbyObjects.Any(col => col.gameObject.CompareTag("plus")))
            ProvideReward(GridGoal.GreenPlus);
        else if (nearbyObjects.Any(col => col.gameObject.CompareTag("ex")))
            ProvideReward(GridGoal.RedEx);
        else if (nearbyObjects.Any(col => col.gameObject.CompareTag("star")))
            ProvideReward(GridGoal.YellowStar);
    }

    private void ProvideReward(GridGoal hitGoal)
    {
        if (CurrentGoal == hitGoal)
        {
            float reward = hasGreenPowerup ? 3f : 1f;
            SetReward(reward);
            visitedZones.Add(GetCurrentZone());
        }
        else
        {
            SetReward(-1f);
        }
        EndEpisode();
    }

    public override void OnEpisodeBegin()
    {
        area.AreaReset();
        ResetState();
        
        // Set random goal if no specific goal is set
        if (string.IsNullOrEmpty(currentLTLGoal))
        {
            Array values = Enum.GetValues(typeof(GridGoal));
            CurrentGoal = (GridGoal)values.GetValue(UnityEngine.Random.Range(0, values.Length));
        }
    }

    private void ResetState()
    {
        episodeEnded = false;
        episodeReward = 0f;
        visitedZones = new HashSet<string>();
        hasGreenPowerup = false;
        currentMoveSpeed = 1.0f;
        m_TimeSinceDecision = 0f;
    }

    private string GetCurrentZone()
    {
        switch (CurrentGoal)
        {
            case GridGoal.GreenPlus: return "green";
            case GridGoal.RedEx: return "red";
            case GridGoal.YellowStar: return "yellow";
            default: return "";
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