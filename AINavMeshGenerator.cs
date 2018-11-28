using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Unity.Jobs;
using Unity.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class Node
{
    public bool valid = true;
    public Vector2 position;
    public readonly Node[] connections;
    public float gCost;
    public float hCost;
    public float fCost { get { return gCost + hCost; } }
    public Node parent;
    public GameObject associatedObject;

    public Node(Vector2 pos)
    {
        position = pos;
        connections = new Node[8];
    }

    public void Reset()
    {
        valid = true;
        associatedObject = null;
        gCost = 0;
        hCost = 0;
        parent = null;
    }

    public bool AnyConnectionsBad()
    {
        for (int i = 0; i < connections.Length; i++)
        {
            if(connections[i] == null || !connections[i].valid)
            {
                return true;
            }
        }
        return false;
    }
}

[ExecuteInEditMode]
public class AINavMeshGenerator : MonoBehaviour
{
    enum Directions { Right, DownRight, Down, DownLeft, Left, UpLeft, Up, UpRight }

    [SerializeField]
    private float updateInterval = 0.1f;
    [SerializeField]
    private float pointDistributionSize = 0.5f;
    [SerializeField]
    LayerMask destroyNodeMask;
    [SerializeField]
    LayerMask obstacleMask;

    public Rect size;
    public static AINavMeshGenerator instance;

    private float updateTimer = 0;
    private List<Node> grid = null;
    private List<Node> Grid
    {
        get
        {
            if(grid == null)
            {
                GenerateNewGrid();
            }
            return grid;
        }
    }
    private Dictionary<Vector2, Node> positionNodeDictionary = new Dictionary<Vector2, Node>();
    public static Pathfinder pathfinder = null;

    public void GenerateNewGrid()
    {
        FillOutGrid();
        DestroyBadNodes();
        CheckForBadNodes();
    }


    public LayerMask GetAvoidanceMasks()
    {
        return destroyNodeMask | obstacleMask;
    }

    private void Awake()
    {
        instance = this;
        pathfinder = new Pathfinder(this);
    }

    private void Start()
    {
        GenerateNewGrid();
        updateTimer = updateInterval;
    }

    private void FillOutGrid()
    {
        grid = new List<Node>();
        positionNodeDictionary.Clear();
        Vector2 currentPoint = new Vector2((size.x - size.width / 2) + pointDistributionSize, (size.y + size.height / 2) - pointDistributionSize);
        int iteration = 0;
        bool alternate = false;
        bool cacheIteration = false;
        int length = -1;
        int yLength = 0;
        while (true)
        {
            iteration++;
            Node newNode = new Node(currentPoint);
            Grid.Add(newNode);
            positionNodeDictionary.Add(currentPoint, newNode);
            currentPoint += new Vector2(pointDistributionSize * 2, 0);
            if (currentPoint.x > size.x + size.width / 2)
            {
                if(length != -1)
                {
                    while(iteration < length)
                    {
                        Node extraNode = new Node(currentPoint);
                        Grid.Add(extraNode);
                        iteration++;
                    }
                }
                else
                {
                    Node extraNode = new Node(currentPoint);
                    Grid.Add(extraNode);
                }
                currentPoint = new Vector2((size.x - size.width / 2) + (alternate ? pointDistributionSize : 0), currentPoint.y - pointDistributionSize);
                alternate = !alternate;
                cacheIteration = true;
                yLength++;
            }
            if (currentPoint.y < size.y - size.height / 2)
            {
                break;
            }
            if(cacheIteration)
            {
                if(length == -1)
                {
                    length = iteration + 1;
                }
                iteration = 0;
                cacheIteration = false;
            }
        }
        for (int i = 0; i < Grid.Count; i++)
        {
            for (int direction = 0; direction < Grid[i].connections.Length; direction++)
            {
                Grid[i].connections[direction] = GetNodeFromDirection(i, (Directions)direction, length);
            }
        }
    }

    private void DestroyBadNodes()
    {
        //First check if each node is inside a destroy mask
        for (int i = Grid.Count - 1; i >= 0; i--)
        {
            Collider2D hit = Physics2D.OverlapCircle(Grid[i].position, 0.01f, destroyNodeMask);
            if (hit != null)
            {
                //At this point, we know this node is bad, and we must destroy it. For humanity itself.
                for (int j = 0; j < Grid[i].connections.Length; j++)
                {
                    //Go through all the connections to this node
                    if (Grid[i].connections[j] != null)
                    {
                        for (int k = 0; k < Grid[i].connections[j].connections.Length; k++)
                        {
                            //Set the nodes connections reference to this node to null, because it no longer exists.
                            //Is that confusing? It sounds confusing.
                            if (Grid[i].connections[j].connections[k] != null)
                            {
                                if (Grid[i].connections[j].connections[k] == Grid[i])
                                {
                                    Grid[i].connections[j].connections[k] = null;
                                }
                            }
                        }
                    }
                }
                Grid.RemoveAt(i);
            }
        }
    }
    
    private void CheckForBadNodes()
    {
        for (int i = 0; i < Grid.Count; i++)
        {
            Grid[i].Reset();
        }

        //Make any node with a destroyed outside have an extra layer barrier around it, so that they dont get too close to walls
        for (int i = 0; i < Grid.Count; i++)
        {
            if (Grid[i].valid)
            {
                for (int j = 0; j < Grid[i].connections.Length; j++)
                {
                    Node connection = Grid[i].connections[j];
                    if (connection == null)
                    {
                        Grid[i].valid = false;
                    }
                }
            }
        }

        //Then check if the node is inside a normal mask to disable it.
        for (int i = 0; i < Grid.Count; i++)
        {
            if(Grid[i].valid)
            {
                Collider2D hit = Physics2D.OverlapCircle(Grid[i].position, 0.05f, obstacleMask);
                if (hit != null)
                {
                    Grid[i].valid = false;
                    Grid[i].associatedObject = hit.transform.gameObject;
                }
            }
        }
    }

    Node GetNodeFromDirection(int nodeIndex, Directions direction, int length)
    {
        int index = -1;
        bool isStartOfRow = (nodeIndex + 1) % length == 1;
        bool isEndOfRow = (nodeIndex + 1) % length == 0;
        bool isOddRow = (((nodeIndex + 1) - Mathf.FloorToInt((nodeIndex) % length)) / length) % 2 == 0;

        switch (direction)
        {
            case Directions.Right:
                if (isEndOfRow) return null;
                index = nodeIndex + 1;
                break;
            case Directions.DownRight:
                if (isEndOfRow && isOddRow) return null;
                index = nodeIndex + length + (isOddRow ? 1 : 0);
                break;
            case Directions.Down:
                index = nodeIndex + length * 2;
                break;
            case Directions.DownLeft:
                if (isStartOfRow && !isOddRow) return null;
                index = nodeIndex + (length - (isOddRow ? 0 : 1));
                break;
            case Directions.Left:
                if (isStartOfRow) return null;
                index = nodeIndex - 1;
                break;
            case Directions.UpLeft:
                if (isStartOfRow && !isOddRow) return null;
                index = nodeIndex - (length + (isOddRow ? 0 : 1));
                break;
            case Directions.Up:
                index = nodeIndex - length * 2;
                break;
            case Directions.UpRight:
                if (isEndOfRow && isOddRow) return null;
                index = nodeIndex - (length - (isOddRow ? 1 : 0));
                break;
        }

        if (index >= 0 && index < Grid.Count)
        {
            return Grid[index];
        }
        else
        {
            return null;
        }
    }

    public Node FindClosestNode(Vector2 position, bool mustBeGood = false, GameObject associatedObject = null)
    {
        Node closest = null;
        float current = float.MaxValue;
        for (int i = 0; i < Grid.Count; i++)
        {
            if(!mustBeGood || Grid[i].valid || associatedObject == Grid[i].associatedObject)
            {
                float distance = Vector2.Distance(Grid[i].position, position);
                if (distance < current)
                {
                    current = distance;
                    closest = Grid[i];
                }
            }
        }

        return closest;
    }

    public void ClearGrid()
    {
        Grid.Clear();
    }

    void Update()
    {
        if(Grid == null)
        {
            GenerateNewGrid();
        }

        //We update the bad nodes constantly, so as objects or enemies move, the grid automatically adjusts itself.
        updateTimer -= Time.deltaTime;
        if(updateTimer <= 0)
        {
            updateTimer = updateInterval;
            CheckForBadNodes();
        }
    }

    void OnDrawGizmosSelected()
    {
        if(Grid == null)
        {
            return;
        }
        for (int i = 0; i < Grid.Count; i++)
        {
            for (int j = 0; j < Grid[i].connections.Length; j++)
            {
                if(Grid[i].connections[j] != null)
                {
                    Gizmos.color = Grid[i].valid && Grid[i].connections[j].valid ? Color.green : Color.red;
                    Gizmos.DrawLine(Grid[i].position, Grid[i].connections[j].position);
                }
            }
        }
        for (int i = 0; i < Grid.Count; i++)
        {
            Gizmos.color = Grid[i].valid ? Color.blue : Color.red;
            Gizmos.DrawCube(Grid[i].position, Vector3.one * 0.2f);
        }
    }
}

public class Pathfinder
{
    AINavMeshGenerator generator;

    List<Node> openSet = new List<Node>();
    HashSet<Node> closedSet = new HashSet<Node>();
    List<Node> path = new List<Node>();
    public Pathfinder(AINavMeshGenerator gen)
    {
        generator = gen;
    }

    private float Heuristic(Node one, Node two)
    {
        return Vector2.Distance(one.position, two.position);
    }

    private Node[] GetAStar(Vector2 currentPosition, Vector2 destination, GameObject obj = null)
    {
        Node start = generator.FindClosestNode(currentPosition, false, obj);
        Node end = generator.FindClosestNode(destination, true, obj);
        if (start == null || end == null)
        {
            return null;
        }

        openSet.Clear();
        closedSet.Clear();
        openSet.Add(start);
        while (openSet.Count > 0)
        {
            Node current = openSet[0];

            //Evaluate costs
            for (int i = 1; i < openSet.Count; i++)
            {
                if (openSet[i].fCost < current.fCost || openSet[i].fCost == current.fCost)
                {
                    if (openSet[i].hCost < current.hCost)
                    {
                        current = openSet[i];
                    }
                }
            }

            openSet.Remove(current);
            closedSet.Add(current);

            if (current.Equals(end))
            {
                break;
            }

            //Go through neighbors
            foreach (Node neighbor in current.connections.Where(x => x != null))
            {
                //The associated object check is so the enemy ignores pathing through it's own bad sector
                if ((!neighbor.valid && neighbor.associatedObject != obj) || closedSet.Contains(neighbor))
                {
                    continue;
                }

                float newCost = current.gCost + Heuristic(current, neighbor);
                if (newCost < neighbor.gCost || !openSet.Contains(neighbor))
                {
                    neighbor.gCost = newCost;
                    neighbor.hCost = Heuristic(neighbor, end);
                    neighbor.parent = current;

                    if (!openSet.Contains(neighbor))
                    {
                        openSet.Add(neighbor);
                    }
                }
            }
        }

        if(end.parent == null)
        {
            return null;
        }

        //Calculate path
        path.Clear();
        Node currentCheck = end;
        while (!path.Contains(currentCheck) && currentCheck != null)
        {
            path.Add(currentCheck);
            currentCheck = currentCheck.parent;
        }
        path.Reverse();
        return path.ToArray();
    }

    private List<Vector2> ShortenPointsByVisibility(Node[] points)
    {
        //If we have small amount of points, dont bother with all this.
        if(points.Length < 2)
        {
            List<Vector2> p = new List<Vector2>();
            for (int i = 0; i < points.Length; i++)
            {
                p.Add(points[i].position);
            }
            return p;
        }

        List<Vector2> corners = new List<Vector2>();
        corners.Add(points[0].position);
        Node start = points[0];
        Node end = points[1];
        //Go through all the points, starting at 1 (since we already set our initial start to the first point)
        for (int i = 1; i < points.Length; i++)
        {
            //Set the end to our current point and check if its in a bad spot to hang out in town.
            end = null;
            end = points[i];
            bool inBadArea = end.AnyConnectionsBad();

            if(inBadArea)
            {
                //If it's a bad boy, we add it to our corners, so we walk this way.
                corners.Add(end.position);
                //Then start anew. 
                start = null;
                start = end;
            }
        }
        //Add that last rebel into the mix for sure.
        corners.Add(points[points.Length - 1].position);

        return corners;
    }

    public Vector2[] FindPath(Vector2 currentPosition, Vector2 destination, GameObject associatedObject = null)
    {
        Node[] points = GetAStar(currentPosition, destination, associatedObject);
        if(points == null)
        {
            return null;
        }

        List<Vector2> shortPath = ShortenPointsByVisibility(points);
        //shortPath.Insert(0, currentPosition);
        return shortPath.ToArray();
    }
}
#if UNITY_EDITOR
[CustomEditor(typeof(AINavMeshGenerator))]
public class AINavMeshGeneratorEditor : Editor
{
    void OnSceneGUI()
    {
        AINavMeshGenerator source = target as AINavMeshGenerator;

        Rect rect = RectUtils.ResizeRect(source.size,
            Handles.CubeHandleCap,
            Color.green, new Color(1, 1, 1, 0.5f),
            HandleUtility.GetHandleSize(Vector3.zero) * 0.1f,
            0.1f);

        source.size = rect;
    }

    public override void OnInspectorGUI()
    {
        AINavMeshGenerator source = target as AINavMeshGenerator;
        base.OnInspectorGUI();
        if (GUILayout.Button("Generate grid"))
        {
            source.GenerateNewGrid();
        }
        if(GUILayout.Button("Clear grid"))
        {
            source.ClearGrid();
        }
    }
}

public class RectUtils
{
    public static Rect ResizeRect(Rect rect, Handles.CapFunction capFunc, Color capCol, Color fillCol, float capSize, float snap)
    {
        Vector2 halfRectSize = new Vector2(rect.size.x * 0.5f, rect.size.y * 0.5f);

        Vector3[] rectangleCorners =
            {
                new Vector3(rect.position.x - halfRectSize.x, rect.position.y - halfRectSize.y, 0),   // Bottom Left
                new Vector3(rect.position.x + halfRectSize.x, rect.position.y - halfRectSize.y, 0),   // Bottom Right
                new Vector3(rect.position.x + halfRectSize.x, rect.position.y + halfRectSize.y, 0),   // Top Right
                new Vector3(rect.position.x - halfRectSize.x, rect.position.y + halfRectSize.y, 0)    // Top Left
            };

        Handles.color = fillCol;
        Handles.DrawSolidRectangleWithOutline(rectangleCorners, new Color(fillCol.r, fillCol.g, fillCol.b, 0.25f), capCol);

        Vector3[] handlePoints =
            {
                new Vector3(rect.position.x - halfRectSize.x, rect.position.y, 0),   // Left
                new Vector3(rect.position.x + halfRectSize.x, rect.position.y, 0),   // Right
                new Vector3(rect.position.x, rect.position.y + halfRectSize.y, 0),   // Top
                new Vector3(rect.position.x, rect.position.y - halfRectSize.y, 0)    // Bottom 
            };

        Handles.color = capCol;

        var newSize = rect.size;
        var newPosition = rect.position;

        var leftHandle = Handles.Slider(handlePoints[0], -Vector3.right, capSize, capFunc, snap).x - handlePoints[0].x;
        var rightHandle = Handles.Slider(handlePoints[1], Vector3.right, capSize, capFunc, snap).x - handlePoints[1].x;
        var topHandle = Handles.Slider(handlePoints[2], Vector3.up, capSize, capFunc, snap).y - handlePoints[2].y;
        var bottomHandle = Handles.Slider(handlePoints[3], -Vector3.up, capSize, capFunc, snap).y - handlePoints[3].y;

        newSize = new Vector2(
            Mathf.Max(.1f, newSize.x - leftHandle + rightHandle),
            Mathf.Max(.1f, newSize.y + topHandle - bottomHandle));

        newPosition = new Vector2(
            newPosition.x + leftHandle * .5f + rightHandle * .5f,
            newPosition.y + topHandle * .5f + bottomHandle * .5f);

        return new Rect(newPosition.x, newPosition.y, newSize.x, newSize.y);
    }
}

#endif
