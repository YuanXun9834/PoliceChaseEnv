using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Unity.MLAgents;
using UnityEngine.Serialization;

public class GridArea : MonoBehaviour
{
    [HideInInspector]
    public List<GameObject> actorObjs;
    [HideInInspector]
    public int[] players;

    public GameObject trueAgent;

    Camera m_AgentCam;

    [FormerlySerializedAs("PlusPref")] public GameObject GreenPlusPrefab;
    [FormerlySerializedAs("ExPref")] public GameObject RedExPrefab;
    public GameObject YellowStarPrefab;

    private const float AGENT_START_X = 1.0f;
    private const float AGENT_START_Z = 1.0f;
    
    // Static positions for goals
    private readonly Vector3 RED_POSITION = new Vector3(2.0f, -0.25f, 2.0f);
    private readonly Vector3 YELLOW_POSITION = new Vector3(3.0f, -0.25f, 3.0f);
    private readonly Vector3 GREEN_POSITION = new Vector3(4.0f, -0.25f, 4.0f);

    GameObject m_Plane;
    GameObject m_Sn;
    GameObject m_Ss;
    GameObject m_Se;
    GameObject m_Sw;

    Vector3 m_InitialPosition;
    EnvironmentParameters m_ResetParams;

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
        var gridSize = (int)m_ResetParams.GetWithDefault("gridSize", 5f);
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

        // Place goals in fixed positions
        PlaceStaticGoals();

        // Place agent in starting position
        trueAgent.transform.localPosition = new Vector3(AGENT_START_X, -0.25f, AGENT_START_Z);
    }

    private void PlaceStaticGoals()
    {
        // Place Red Goal (Closest)
        var redGoal = Instantiate(RedExPrefab, transform);
        redGoal.transform.localPosition = RED_POSITION;
        redGoal.tag = "ex";
        actorObjs.Add(redGoal);

        // Place Yellow Goal (Middle)
        var yellowGoal = Instantiate(YellowStarPrefab, transform);
        yellowGoal.transform.localPosition = YELLOW_POSITION;
        yellowGoal.tag = "star";
        actorObjs.Add(yellowGoal);

        // Place Green Goal (Furthest)
        var greenGoal = Instantiate(GreenPlusPrefab, transform);
        greenGoal.transform.localPosition = GREEN_POSITION;
        greenGoal.tag = "plus";
        actorObjs.Add(greenGoal);
    }
}