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
    GameObject[] m_Objects;
    public int numberOfPlus = 1;
    public int numberOfEx = 1;
    public int numberOfYellow = 1;

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

        // Update objects array to include YellowStarPrefab
        m_Objects = new[] { GreenPlusPrefab, RedExPrefab, YellowStarPrefab };

        m_AgentCam = transform.Find("agentCam").GetComponent<Camera>();

        actorObjs = new List<GameObject>();

        var sceneTransform = transform.Find("scene");

        m_Plane = sceneTransform.Find("Plane").gameObject;
        m_Sn = sceneTransform.Find("sN").gameObject;
        m_Ss = sceneTransform.Find("sS").gameObject;
        m_Sw = sceneTransform.Find("sW").gameObject;
        m_Se = sceneTransform.Find("sE").gameObject;
        m_InitialPosition = transform.position;
    }

    void SetEnvironment()
    {
        transform.position = m_InitialPosition * (m_ResetParams.GetWithDefault("gridSize", 5f) + 1);
        var playersList = new List<int>();

        // Add indices for each type of goal
        for (var i = 0; i < (int)m_ResetParams.GetWithDefault("numPlusGoals", numberOfPlus); i++)
        {
            playersList.Add(0); // Green Plus
        }

        for (var i = 0; i < (int)m_ResetParams.GetWithDefault("numExGoals", numberOfEx); i++)
        {
            playersList.Add(1); // Red Ex
        }

        for (var i = 0; i < (int)m_ResetParams.GetWithDefault("numYellowGoals", numberOfYellow); i++)
        {
            playersList.Add(2); // Yellow Star
        }

        players = playersList.ToArray();

        var gridSize = (int)m_ResetParams.GetWithDefault("gridSize", 5f);
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
        // Add delay before reset
        StartCoroutine(DelayedReset());
    }

private System.Collections.IEnumerator DelayedReset()
{
    yield return new WaitForSeconds(1.0f);

    var gridSize = (int)m_ResetParams.GetWithDefault("gridSize", 5f);
    foreach (var actor in actorObjs)
    {
        DestroyImmediate(actor);
    }
    SetEnvironment();

    actorObjs.Clear();

    // Create a list of all possible positions
    List<Vector2Int> availablePositions = new List<Vector2Int>();
    for (int x = 0; x < gridSize; x++)
    {
        for (int z = 0; z < gridSize; z++)
        {
            availablePositions.Add(new Vector2Int(x, z));
        }
    }

    // Shuffle available positions
    for (int i = availablePositions.Count - 1; i > 0; i--)
    {
        int j = Random.Range(0, i + 1);
        var temp = availablePositions[i];
        availablePositions[i] = availablePositions[j];
        availablePositions[j] = temp;
    }

    // Place goals with correct positioning
    int posIndex = 0;
    for (var i = 0; i < players.Length; i++)
    {
        float safeBuffer = 0.5f;
        var pos = availablePositions[posIndex++];
        var actorObj = Instantiate(m_Objects[players[i]], transform);
        // Ensure position is not too close to edges
        Vector3 safePosition = new Vector3(
            Mathf.Clamp(pos.x, safeBuffer, gridSize - safeBuffer),
            -0.25f,
            Mathf.Clamp(pos.y, safeBuffer, gridSize - safeBuffer)
        );
        actorObj.transform.localPosition = safePosition;
        var movingGoal = actorObj.AddComponent<MovingGoal>();
        movingGoal.Initialize(gridSize);
        actorObjs.Add(actorObj);

        switch (players[i])
        {
            case 0:
                actorObj.tag = "plus";
                break;
            case 1:
                actorObj.tag = "ex";
                break;
            case 2:
                actorObj.tag = "star";
                break;
        }
    }

    // Place agent at integer coordinates
    var agentPos = availablePositions[posIndex];
    trueAgent.transform.localPosition = new Vector3(agentPos.x, -0.25f, agentPos.y);
}
}