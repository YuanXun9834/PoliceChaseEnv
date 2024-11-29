using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.SideChannels;
using System.Collections.Generic;
using System.Linq;
using System;
public class GoalSequenceChannel : SideChannel
{
    public GoalSequenceChannel()
    {
        Debug.Log($"Side channel created. ID will be set by Python.");
    }

    protected override void OnMessageReceived(IncomingMessage message)
    {
        Debug.Log($"[{ChannelId:X}] Received sequence message");
        try {
            var sequenceLength = message.ReadInt32();
            Debug.Log($"[{ChannelId:X}] Message length: {sequenceLength}");
            
            var sequence = new List<GCRLLTLGridAgent.GridGoal>();
            for (int i = 0; i < sequenceLength; i++)
            {
                var goalInt = message.ReadInt32();
                sequence.Add((GCRLLTLGridAgent.GridGoal)goalInt);
                Debug.Log($"[{ChannelId:X}] Goal {i}: {(GCRLLTLGridAgent.GridGoal)goalInt}");
            }

            var agent = GameObject.FindObjectOfType<GCRLLTLGridAgent>();
            if (agent != null)
            {
                agent.SetGoalSequence(sequence);
                Debug.Log($"[{ChannelId:X}] Sequence set successfully");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[{ChannelId:X}] Error: {e.Message}");
        }
    }
}
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
    
    [Header("Movement Settings")]
    [SerializeField] private float baseMovementScale = 0.25f;
    [SerializeField] private float powerupSpeedMultiplier = 2.0f;

    // Constants
    private const float AGENT_SCALE = 0.3f;
    private const float MOVEMENT_PRECISION = 0.25f;
    private const float POSITION_TOLERANCE = 0.1f;
    private const float GOAL_REACH_THRESHOLD = 0.3f;
    
    private const int k_Up = 0;     // Was 1
    private const int k_Down = 1;   // Was 2 
    private const int k_Left = 2;   // Was 3
    private const int k_Right = 3;  // Was 4

    // Constants - Improved reward structure
    private const float GOAL_REACH_REWARD = 10.0f;  // Increased
    private const float SEQUENCE_COMPLETION_REWARD = 20.0f;  // Increased
    private const float WALL_COLLISION_PENALTY = -0.5f;
    private const float STEP_PENALTY = -0.001f;  // Reduced to prevent early termination
    private const float DISTANCE_REWARD_SCALE = 0.1f;  // New: reward for getting closer to goal
    private const float EXPLORATION_BONUS = 1.0f;  // New: reward for exploring new positions
    private const float POWERUP_BONUS = 5.0f;  // New: increased bonus for getting powerup
    // Add new tracking variables
    private Vector3 previousPosition;  // Add this line for position tracking
    private Vector3 previousGoalDistance;
    private float closestDistanceToGoal = float.MaxValue;
    private int stepsWithoutProgress = 0;
    private const int MAX_STEPS_WITHOUT_PROGRESS = 100;

    private int lastAction = -1;  // Initialize to -1 to indicate no previous action
    private int consecutiveSameActions = 0;
    private const int MAX_SAME_ACTION_THRESHOLD = 10;

    // State tracking
    private Vector3 lastPosition;
    private bool movementAttempted;

    private HashSet<Vector2Int> visitedPositions;
    private Queue<Vector3> recentPositions;
    private const int OSCILLATION_WINDOW = 5;
    private Vector3 lastGoalDirection;

    // Components and state tracking
    private LTLGoalSideChannel ltlGoalChannel;
    private VectorSensorComponent stateSensor;
    private EnvironmentParameters m_ResetParams;
    private float m_TimeSinceDecision;
    private bool hasGreenPowerup;
    private Queue<GridGoal> targetGoalSequence;
    private List<GridGoal> achievedGoals;
    private GridGoal? currentTargetGoal;
    private int currentStepCount;
    private bool episodeEnded;

    public enum GridGoal
    {
        RedEx = 0,      // Make sure these match Python indices
        GreenPlus = 1,
        YellowStar = 2
    }

    private GridGoal m_CurrentGoal;
    public GridGoal CurrentGoal
    {
        get { return m_CurrentGoal; }
        set
        {
            m_CurrentGoal = value;
            
            try 
            {
                // Update visual indicators with verification
                bool greenActive = value == GridGoal.GreenPlus;
                bool redActive = value == GridGoal.RedEx;
                bool yellowActive = value == GridGoal.YellowStar;
                
                if (GreenBottom != null) GreenBottom.SetActive(greenActive);
                if (RedBottom != null) RedBottom.SetActive(redActive);
                if (YellowBottom != null) YellowBottom.SetActive(yellowActive);
                
                Debug.Log($"\n=== Goal Update ===");
                Debug.Log($"Set goal to: {value}");
                Debug.Log($"Indicators: Green={greenActive}, Red={redActive}, Yellow={yellowActive}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error updating goal indicators: {e.Message}\n{e.StackTrace}");
            }
        }
    }

    public override void Initialize()
    {
        base.Initialize();
        Debug.Log("Initializing GCRLLTLGridAgent...");
        
        if (Academy.Instance.IsCommunicatorOn)
        {
            ltlGoalChannel = new LTLGoalSideChannel();
            var goalSequenceChannel = new GoalSequenceChannel();
            SideChannelManager.RegisterSideChannel(ltlGoalChannel);
            SideChannelManager.RegisterSideChannel(goalSequenceChannel);
        }

        m_ResetParams = Academy.Instance.EnvironmentParameters;
        transform.localScale = Vector3.one * AGENT_SCALE;
        
        // Initialize collections
        achievedGoals = new List<GridGoal>();
        targetGoalSequence = new Queue<GridGoal>();
        visitedPositions = new HashSet<Vector2Int>();
        previousGoalDistance = Vector3.zero;
    }

    public override void OnEpisodeBegin()
    {
        Debug.Log("Starting new episode");

        // Reset environment
        area.AreaReset();

        // Reset agent state
        hasGreenPowerup = false;
        m_TimeSinceDecision = 0f;
        currentStepCount = 0;
        episodeEnded = false;
        achievedGoals.Clear();

        // Reset position tracking
        previousPosition = transform.position;
        previousGoalDistance = Vector3.zero;
        closestDistanceToGoal = float.MaxValue;
        stepsWithoutProgress = 0;

        // Initialize state tracking
        visitedPositions = new HashSet<Vector2Int>();
        recentPositions = new Queue<Vector3>();
        lastGoalDirection = Vector3.zero;
        lastAction = -1;
        consecutiveSameActions = 0;

        // Handle goal sequence initialization with improved logging
        Debug.Log("\n=== Goal Sequence State ===");
        Debug.Log($"Current Queue Count: {targetGoalSequence?.Count ?? 0}");
        
        if (targetGoalSequence == null || targetGoalSequence.Count == 0)
        {
            Debug.Log("Waiting for goal sequence from Python side...");
            currentTargetGoal = null;
            CurrentGoal = 0; // Default to first goal type
        }
        else
        {
            currentTargetGoal = targetGoalSequence.Peek();
            if (currentTargetGoal.HasValue)
            {
                CurrentGoal = currentTargetGoal.Value;
                Debug.Log($"Goal Sequence: {string.Join(" -> ", targetGoalSequence.ToArray())}");
                Debug.Log($"Setting Current Goal: {CurrentGoal}");
                Debug.Log($"Goal One-Hot will be: [{(CurrentGoal == GridGoal.RedEx ? 1 : 0)}, " +
                        $"{(CurrentGoal == GridGoal.GreenPlus ? 1 : 0)}, " +
                        $"{(CurrentGoal == GridGoal.YellowStar ? 1 : 0)}]");
            }
            else
            {
                Debug.LogError("Invalid goal in sequence!");
            }
        }

        // Verify goal indicators are set correctly
        Debug.Log("\n=== Goal Indicator State ===");
        Debug.Log($"Green Indicator: {GreenBottom?.activeSelf}");
        Debug.Log($"Red Indicator: {RedBottom?.activeSelf}");
        Debug.Log($"Yellow Indicator: {YellowBottom?.activeSelf}");
    }
    // Add a method to broadcast current goal state
    private void BroadcastGoalState()
    {
        Debug.Log($"Current Goal State: {CurrentGoal}");
        Debug.Log($"Achieved Goals: {string.Join(",", achievedGoals)}");
        Debug.Log($"Remaining Sequence: {string.Join(" -> ", targetGoalSequence)}");
    }

    // Update goal setting to be more robust
    public void SetGoalSequence(List<GridGoal> sequence)
    {
        Debug.Log($"Setting new goal sequence. Count: {sequence?.Count ?? 0}");
        
        if (sequence == null || sequence.Count == 0)
        {
            Debug.LogError("Received empty or null sequence!");
            return;
        }

        // Clear existing state
        targetGoalSequence.Clear();
        achievedGoals.Clear();
        
        foreach (var goal in sequence)
        {
            targetGoalSequence.Enqueue(goal);
        }
        
        // Set initial goal
        currentTargetGoal = targetGoalSequence.Count > 0 ? targetGoalSequence.Peek() : null;
        
        if (currentTargetGoal.HasValue)
        {
            CurrentGoal = currentTargetGoal.Value;
            Debug.Log($"Initial goal set to: {CurrentGoal}");
        }
    }

    private void UpdateCurrentGoal()
    {
        if (targetGoalSequence.Count > 0)
        {
            CurrentGoal = targetGoalSequence.Peek();
            Debug.Log($"Set current goal to: {CurrentGoal}");
        }
        else
        {
            Debug.Log("No more goals in sequence");
        }
    }

    private void CheckGoalAchievement()
    {
        // Debug log current state
        Debug.Log($"Checking goals - Current: {currentTargetGoal}, Queue Size: {targetGoalSequence?.Count ?? 0}");
        
        if (!currentTargetGoal.HasValue || episodeEnded)
        {
            Debug.Log("No current goal or episode ended");
            return;
        }

        var nearbyObjects = Physics.OverlapSphere(transform.position, GOAL_REACH_THRESHOLD);
        foreach (var collider in nearbyObjects)
        {
            GridGoal? touchedGoal = GetTouchedGoal(collider);
            if (!touchedGoal.HasValue) continue;

            Debug.Log($"Touching goal: {touchedGoal.Value}, Target: {currentTargetGoal.Value}");

            if (touchedGoal.Value == currentTargetGoal.Value)
            {
                Debug.Log($"Goal achieved: {touchedGoal.Value}!");
                
                // Add reward and mark achieved
                AddReward(GOAL_REACH_REWARD);
                achievedGoals.Add(touchedGoal.Value);
                
                // Despawn goal
                string goalTag = GetGoalTag(touchedGoal.Value);
                Debug.Log($"Despawning goal with tag: {goalTag}");
                area.DespawnGoal(goalTag);

                // Reset tracking for next goal
                visitedPositions.Clear();
                recentPositions.Clear();
                lastGoalDirection = Vector3.zero;

                // Move to next goal
                targetGoalSequence.Dequeue();
                if (targetGoalSequence.Count > 0)
                {
                    currentTargetGoal = targetGoalSequence.Peek();
                    CurrentGoal = currentTargetGoal.Value;
                    Debug.Log($"New goal set: {CurrentGoal}");
                }
                else
                {
                    // Handle sequence completion rewards
                    if (achievedGoals.Count == 2)
                    {
                        AddReward(5.0f);
                        Debug.Log("2 goals achieved bonus!");
                    }
                    else if (achievedGoals.Count == 3)
                    {
                        AddReward(SEQUENCE_COMPLETION_REWARD);
                        Debug.Log("Full sequence completed!");
                        episodeEnded = true;
                        EndEpisode();
                    }
                }

                if (touchedGoal.Value == GridGoal.GreenPlus)
                {
                    hasGreenPowerup = true;
                    AddReward(POWERUP_BONUS); 
                }

                break;
            }
        }
    }

    private GridGoal? GetTouchedGoal(Collider collider)
    {
        if (collider.transform.IsChildOf(transform)) return null;
        if (collider.CompareTag("plus")) return GridGoal.GreenPlus;
        if (collider.CompareTag("ex")) return GridGoal.RedEx;
        if (collider.CompareTag("star")) return GridGoal.YellowStar;
        return null;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Agent position (2)
        Debug.Log($"Adding agent position: ({transform.localPosition.x}, {transform.localPosition.z})");
        sensor.AddObservation(transform.localPosition.x);
        sensor.AddObservation(transform.localPosition.z);
        
        Debug.Log("\nCollecting Observations:");
        Debug.Log($"Current Target Goal: {currentTargetGoal}");
        Debug.Log($"Remaining Goals in Sequence: {string.Join(" -> ", targetGoalSequence)}");
        Debug.Log($"Achieved Goals: {string.Join(", ", achievedGoals)}");
        // Current goal one-hot (3) - Fixed logic to match enum order
        var redGoal = currentTargetGoal == GridGoal.RedEx;
        var greenGoal = currentTargetGoal == GridGoal.GreenPlus;
        var yellowGoal = currentTargetGoal == GridGoal.YellowStar;
        
        Debug.Log($"Current target goal: {currentTargetGoal}");
        Debug.Log($"Adding one-hot encoding: [{(redGoal ? 1 : 0)}, {(greenGoal ? 1 : 0)}, {(yellowGoal ? 1 : 0)}]");
        
        // Important: Add observations in same order as enum
        sensor.AddObservation(currentTargetGoal == GridGoal.RedEx ? 1f : 0f);
        sensor.AddObservation(currentTargetGoal == GridGoal.GreenPlus ? 1f : 0f);
        sensor.AddObservation(currentTargetGoal == GridGoal.YellowStar ? 1f : 0f);
        
        // Powerup status (1)
        Debug.Log($"Adding powerup status: {hasGreenPowerup}");
        sensor.AddObservation(hasGreenPowerup ? 1f : 0f);
        
        // Achieved goals (3) - Also fixed to match enum order
        Debug.Log($"Adding achieved goals: Red={achievedGoals.Contains(GridGoal.RedEx)}, " +
                $"Green={achievedGoals.Contains(GridGoal.GreenPlus)}, " +
                $"Yellow={achievedGoals.Contains(GridGoal.YellowStar)}");
                
        sensor.AddObservation(achievedGoals.Contains(GridGoal.RedEx) ? 1f : 0f);
        sensor.AddObservation(achievedGoals.Contains(GridGoal.GreenPlus) ? 1f : 0f);
        sensor.AddObservation(achievedGoals.Contains(GridGoal.YellowStar) ? 1f : 0f);
        
        // Goal positions (6)
        Debug.Log("Adding goal positions:");
        if (area?.actorObjs != null)
        {
            // We should iterate in specific order to match observation space
            var sortedGoals = area.actorObjs
                .Where(g => g != null)
                .OrderBy(g => {
                    if (g.CompareTag("ex")) return 0;      // Red first
                    if (g.CompareTag("plus")) return 1;    // Green second
                    if (g.CompareTag("star")) return 2;    // Yellow third
                    return 3;
                });
                
            foreach (var goal in sortedGoals)
            {
                Debug.Log($"Goal at: ({goal.transform.localPosition.x}, {goal.transform.localPosition.z})");
                sensor.AddObservation(goal.transform.localPosition.x);
                sensor.AddObservation(goal.transform.localPosition.z);
            }
        }
        else
        {
            Debug.Log("No area or actor objects!");
            // Add zeros for missing goals
            for (int i = 0; i < 6; i++)
                sensor.AddObservation(0f);
        }

        // Print final observation count
        Debug.Log($"Total observations added: {sensor.ObservationSize()}");
    }

    // Add this to help track goal state
    private void LogGoalState()
    {
        Debug.Log($"Current Goal State:");
        Debug.Log($"- Current Target Goal: {currentTargetGoal}");
        Debug.Log($"- Current Goal Property: {m_CurrentGoal}");
        Debug.Log($"- Queue Count: {targetGoalSequence?.Count ?? 0}");
        if (targetGoalSequence != null && targetGoalSequence.Count > 0)
        {
            Debug.Log($"- Next in Queue: {targetGoalSequence.Peek()}");
        }
    }


    private float CalculateDistanceReward(Vector3 currentPos, GridGoal goal)
    {
        Vector3 goalPos = GetGoalPosition(goal);
        float currentDistance = Vector3.Distance(currentPos, goalPos);
        float previousDistance = Vector3.Distance(previousPosition, goalPos);
        
        float distanceReward = (previousDistance - currentDistance) * DISTANCE_REWARD_SCALE;
        
        // Update closest distance tracking
        if (currentDistance < closestDistanceToGoal)
        {
            closestDistanceToGoal = currentDistance;
            stepsWithoutProgress = 0;
            return distanceReward + EXPLORATION_BONUS;  // Extra bonus for new best
        }
        
        stepsWithoutProgress++;
        return distanceReward;
    }

    private void AddExplorationBonus(Vector3 currentPos)
    {
        Vector2Int gridPos = new Vector2Int(
            Mathf.RoundToInt(currentPos.x * 2),  // Multiply by 2 for finer grid
            Mathf.RoundToInt(currentPos.z * 2)
        );
        
        if (!visitedPositions.Contains(gridPos))
        {
            visitedPositions.Add(gridPos);
            AddReward(EXPLORATION_BONUS);
        }
    }

    public override void OnActionReceived(ActionBuffers actionBuffers)
    {
        if (episodeEnded) return;
        
        float episodeReward = 0f;
        var previousPosition = transform.position;
        AddReward(STEP_PENALTY);
        episodeReward += STEP_PENALTY;
        
        var action = actionBuffers.DiscreteActions[0];
        Debug.Log($"Action received: {action}");
        // Add penalty for repeated actions
        if (action == lastAction && lastAction != -1) {
            consecutiveSameActions++;
            if (consecutiveSameActions > 3) {
                float repeatPenalty = -0.01f * consecutiveSameActions;
                AddReward(repeatPenalty);
                episodeReward += repeatPenalty;
                Debug.Log($"Added repeat action penalty: {repeatPenalty}");
            }
        } else {
            consecutiveSameActions = 0;
        }
        lastAction = action;
        // Calculate move scale based on powerup status
        var moveScale = hasGreenPowerup ? 
            baseMovementScale * powerupSpeedMultiplier : 
            baseMovementScale;
            
        var currentPos = transform.position;
        var targetPos = currentPos;
        var validMove = true;

        // Process movement
        switch (action)
        {
            case k_Right:
                if (currentPos.x < m_ResetParams.GetWithDefault("gridSize", 5f) - POSITION_TOLERANCE)
                    targetPos += Vector3.right * moveScale;
                else
                    validMove = false;
                break;
            case k_Left:
                if (currentPos.x > POSITION_TOLERANCE)
                    targetPos += Vector3.left * moveScale;
                else
                    validMove = false;
                break;
            case k_Up:
                if (currentPos.z < m_ResetParams.GetWithDefault("gridSize", 5f) - POSITION_TOLERANCE)
                    targetPos += Vector3.forward * moveScale;
                else
                    validMove = false;
                break;
            case k_Down:
                if (currentPos.z > POSITION_TOLERANCE)
                    targetPos += Vector3.back * moveScale;
                else
                    validMove = false;
                break;

        }
        Debug.Log($"Step rewards: Basic:{STEP_PENALTY}, Action:{episodeReward}, Cumulative:{GetCumulativeReward()}");
        // Execute movement and calculate rewards
        if (validMove)
        {
            var hit = Physics.OverlapSphere(targetPos, POSITION_TOLERANCE, LayerMask.GetMask("Wall"));
            if (hit.Length == 0)
            {
                var oldPosition = transform.position;
                transform.position = Vector3.MoveTowards(currentPos, targetPos, moveScale);
                
                // Add movement reward
                float movementDistance = Vector3.Distance(oldPosition, transform.position);
                if(movementDistance > 0.01f) {
                    float movementReward = 0.1f;
                    AddReward(movementReward);
                    episodeReward += movementReward;
                    Debug.Log($"Added movement reward: {movementReward}");
                }
                // Calculate distance-based reward
                if (currentTargetGoal.HasValue)
                {
                    float distanceReward = CalculateDistanceReward(transform.position, currentTargetGoal.Value);
                    AddReward(distanceReward);
                    episodeReward += distanceReward;
                    Debug.Log($"Added distance reward: {distanceReward}");
                }
                
                // Add exploration bonus
                if (!visitedPositions.Contains(GetGridPosition(transform.position)))
                {
                    AddReward(EXPLORATION_BONUS);
                    episodeReward += EXPLORATION_BONUS;
                    Debug.Log($"Added exploration bonus: {EXPLORATION_BONUS}");
                }
            }
            else
            {
                episodeReward += WALL_COLLISION_PENALTY;
                Debug.Log($"Added wall collision penalty: {WALL_COLLISION_PENALTY}");
                AddReward(WALL_COLLISION_PENALTY);
            }
        }

        // Check goal achievement and update rewards
        CheckGoalAchievement();
        
        // Handle timeout and progress checks
        currentStepCount++;
        if (currentStepCount >= maxSteps || stepsWithoutProgress >= MAX_STEPS_WITHOUT_PROGRESS)
        {
            float completionRatio = achievedGoals.Count / 3f;
            AddReward(-5f * (1f - completionRatio));  // Reduced penalty if made progress
            EndEpisode();
        }
        Debug.Log($"Final step rewards: {episodeReward}, Total: {GetCumulativeReward()}");
        // Update position tracking
        previousPosition = transform.position;
    }

    private Vector2Int GetGridPosition(Vector3 position)
    {
        return new Vector2Int(
            Mathf.RoundToInt(position.x * 2),
            Mathf.RoundToInt(position.z * 2)
        );
    }
    // Helper method to get goal position
    private Vector3 GetGoalPosition(GridGoal goal)
    {
        foreach (var obj in area.actorObjs)
        {
            switch (goal)
            {
                case GridGoal.GreenPlus when obj.CompareTag("plus"):
                case GridGoal.RedEx when obj.CompareTag("ex"):
                case GridGoal.YellowStar when obj.CompareTag("star"):
                    return obj.transform.position;
            }
        }
        return Vector3.zero;
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        if (!maskActions) return;

        var posX = (int)transform.localPosition.x;
        var posZ = (int)transform.localPosition.z;
        var maxPos = (int)m_ResetParams.GetWithDefault("gridSize", 5f) - 1;

        if (posX == 0) actionMask.SetActionEnabled(0, k_Left, false);
        if (posX == maxPos) actionMask.SetActionEnabled(0, k_Right, false);
        if (posZ == 0) actionMask.SetActionEnabled(0, k_Down, false);
        if (posZ == maxPos) actionMask.SetActionEnabled(0, k_Up, false);
    }

    private string GetGoalTag(GridGoal goal)
    {
        switch (goal)
        {
            case GridGoal.GreenPlus: return "plus";
            case GridGoal.RedEx: return "ex";
            case GridGoal.YellowStar: return "star";
            default: return "";
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discreteActionsOut = actionsOut.DiscreteActions;
        discreteActionsOut[0] = k_Down; // Set default to something other than 0
        
        if (Input.GetKey(KeyCode.D)) discreteActionsOut[0] = k_Right;
        if (Input.GetKey(KeyCode.W)) discreteActionsOut[0] = k_Up;
        if (Input.GetKey(KeyCode.A)) discreteActionsOut[0] = k_Left;
        if (Input.GetKey(KeyCode.S)) discreteActionsOut[0] = k_Down;
        
        Debug.Log($"Heuristic produced action: {discreteActionsOut[0]}");
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