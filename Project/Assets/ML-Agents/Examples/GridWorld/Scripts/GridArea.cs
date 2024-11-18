using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;
using UnityEngine.Serialization;

public class GridArea : MonoBehaviour
{
    [HideInInspector]
    public List<GameObject> actorObjs;
    
    public GameObject trueAgent;
    Camera m_AgentCam;

    [FormerlySerializedAs("PlusPref")] public GameObject GreenPlusPrefab;
    [FormerlySerializedAs("ExPref")] public GameObject RedExPrefab;
    public GameObject YellowStarPrefab;

    private const float GOAL_SCALE = 0.25f;
    
    // Fixed positions - no randomization
    private readonly Vector3 AGENT_START = new Vector3(1.0f, -0.25f, 1.0f);
    private readonly Vector3 RED_POSITION = new Vector3(2.0f, -0.25f, 2.0f);     // Closest
    private readonly Vector3 YELLOW_POSITION = new Vector3(3.0f, -0.25f, 3.0f);  // Middle
    private readonly Vector3 GREEN_POSITION = new Vector3(4.0f, -0.25f, 3.5f);   // Furthest
    
    GameObject m_Plane;
    GameObject m_Sn;
    GameObject m_Ss;
    GameObject m_Se;
    GameObject m_Sw;

    Vector3 m_InitialPosition;
    EnvironmentParameters m_ResetParams;
    private int gridSize;

    public void Start()
    {
        m_ResetParams = Academy.Instance.EnvironmentParameters;
        m_AgentCam = transform.Find("agentCam").GetComponent<Camera>();
        actorObjs = new List<GameObject>();

        var sceneTransform = transform.Find("scene");
        m_Plane = sceneTransform.Find("Plane").gameObject;
        m_Sn = sceneTransform.Find("sN").gameObject;
        m_Ss = sceneTransform.Find("sS").gameObject;
        m_Sw = sceneTransform.Find("sW").gameObject;
        m_Se = sceneTransform.Find("sE").gameObject;
        m_InitialPosition = transform.position;

        SetupEnvironment();
    }

    void SetupEnvironment()
    {
        gridSize = (int)m_ResetParams.GetWithDefault("gridSize", 5f);
        transform.position = m_InitialPosition * (gridSize + 1);

        // Configure environment boundaries
        m_Plane.transform.localScale = new Vector3(gridSize / 10.0f, 1f, gridSize / 10.0f);
        m_Plane.transform.localPosition = new Vector3((gridSize - 1) / 2f, -0.5f, (gridSize - 1) / 2f);
        m_Sn.transform.localScale = new Vector3(1, 1, gridSize + 2);
        m_Ss.transform.localScale = new Vector3(1, 1, gridSize + 2);
        m_Sn.transform.localPosition = new Vector3((gridSize - 1) / 2f, 0.0f, gridSize);
        m_Ss.transform.localPosition = new Vector3((gridSize - 1) / 2f, 0.0f, -1);
        m_Se.transform.localScale = new Vector3(1, 1, gridSize + 2);
        m_Sw.transform.localScale = new Vector3(1, 1, gridSize + 2);
        m_Se.transform.localPosition = new Vector3(gridSize, 0.0f, (gridSize - 1) / 2f);
        m_Sw.transform.localPosition = new Vector3(-1, 0.0f, (gridSize - 1) / 2f);

        m_AgentCam.orthographicSize = (gridSize) / 2f;
        m_AgentCam.transform.localPosition = new Vector3((gridSize - 1) / 2f, gridSize + 1f, (gridSize - 1) / 2f);
    }

    public void AreaReset()
    {
        foreach (var actor in actorObjs)
        {
            DestroyImmediate(actor);
        }
        actorObjs.Clear();

        PlaceStaticGoals();
        
        // Place agent in fixed starting position
        trueAgent.transform.localPosition = AGENT_START;
    }

    private void PlaceStaticGoals()
    {
        // Place Red Goal (Closest)
        var redGoal = Instantiate(RedExPrefab, transform);
        redGoal.transform.localPosition = RED_POSITION;
        redGoal.transform.localScale = Vector3.one * GOAL_SCALE;
        redGoal.tag = "ex";
        actorObjs.Add(redGoal);

        // Place Yellow Goal (Middle)
        var yellowGoal = Instantiate(YellowStarPrefab, transform);
        yellowGoal.transform.localPosition = YELLOW_POSITION;
        yellowGoal.transform.localScale = Vector3.one * GOAL_SCALE;
        yellowGoal.tag = "star";
        actorObjs.Add(yellowGoal);

        // Place Green Goal (Furthest)
        var greenGoal = Instantiate(GreenPlusPrefab, transform);
        greenGoal.transform.localPosition = GREEN_POSITION;
        greenGoal.transform.localScale = Vector3.one * GOAL_SCALE;
        greenGoal.tag = "plus";
        actorObjs.Add(greenGoal);
    }
}