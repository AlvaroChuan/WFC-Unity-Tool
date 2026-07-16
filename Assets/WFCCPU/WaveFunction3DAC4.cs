using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using Tile3D = WFC3DMapGenerator.Tile3D;
using Tileset = WFC3DMapGenerator.Tileset;

public class WaveFunction3DAC4 : MonoBehaviour
{
    [SerializeField] private int dimensionsX, dimensionsZ, dimensionsY;
    [SerializeField] private Tile3D floorTile;
    [SerializeField] private Tile3D emptyTile;
    [SerializeField] private Tileset tileset;                       //Tileset ScriptableObject (shared with GPU version)
    [SerializeField] private Cell3D cellObj;                        //They can be collapsed or not. Tiles are their children.
    private Tile3D[] tileObjects;                                   //All the map tiles that you can use (populated from tileset)
    private int cellSize;
    private int iterations = 0;
    private List<Cell3D> gridComponents;
    private Stopwatch stopwatch;
    //Events
    public delegate void OnRegenerate();
    public static event OnRegenerate onRegenerate;

    void DestroyHelper(GameObject obj)
    {
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    void Awake()
    {
        if (Application.isPlaying) StartGeneration();
    }

    public void StartGeneration()
    {
        tileObjects = tileset.tiles.ToArray();
        cellSize = (int)tileset.tileSize;

        ClearNeighbours(ref tileObjects);
        CreateRemainingCells(ref tileObjects);
        DefineNeighbourTiles(ref tileObjects, ref tileObjects);

        gridComponents = new List<Cell3D>();

        stopwatch = new Stopwatch();
        stopwatch.Start();
        InitializeGrid();
        CreateSolidFloor();
        CreateSolidCeiling();
        UpdateGeneration();
    }

    private void ClearNeighbours(ref Tile3D[] tileArray)
    {
        foreach (Tile3D tile in tileArray)
        {
            tile.upNeighbours.Clear();
            tile.rightNeighbours.Clear();
            tile.downNeighbours.Clear();
            tile.leftNeighbours.Clear();

            tile.aboveNeighbours.Clear();
            tile.belowNeighbours.Clear();

        }
    }
    
    Tile3D CreateNewTileVariation(Tile3D tile, string nameVariation)
    {
        string name = tile.gameObject.name + nameVariation;
        GameObject newTile = new GameObject(name);
        newTile.gameObject.tag = tile.gameObject.tag;
        newTile.SetActive(false);
        newTile.hideFlags = HideFlags.HideInHierarchy;

        MeshFilter meshFilter = newTile.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = tile.gameObject.GetComponent<MeshFilter>().sharedMesh;
        MeshRenderer meshRenderer = newTile.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterials = tile.gameObject.GetComponent<MeshRenderer>().sharedMaterials;

        Tile3D tileRotated = newTile.AddComponent<Tile3D>();
        tileRotated.tileType = tile.tileType;
        tileRotated.probability = tile.probability;
        tileRotated.positionOffset = tile.positionOffset;

        return tileRotated;
    }
    //---------------Look if we have to create more tiles-------------------
    private void CreateRemainingCells(ref Tile3D[] tileArray)
    {
        List<Tile3D> newTiles = new List<Tile3D>();
        foreach (Tile3D tile in tileArray)
        {
           
            if (tile.rotateRight) //Por defecto, sentido horario
            {
                Tile3D tileRotated = CreateNewTileVariation(tile, "_RotateRight");
               // tile.excludedNeighbours.Add(tileRotated);
                RotateBorders90(tile, tileRotated);

                tileRotated.rotation = new Vector3(0f, 90f, 0f);
                newTiles.Add(tileRotated);
            }

            if (tile.rotate180)
            {
                Tile3D tileRotated = CreateNewTileVariation(tile, "_Rotate180");
              //  tile.excludedNeighbours.Add(tileRotated);
                RotateBorders180(tile, tileRotated);
                tileRotated.rotation = new Vector3(0f, 180f, 0f);
                newTiles.Add(tileRotated);
            }

            if (tile.rotateLeft)
            {
                Tile3D tileRotated = CreateNewTileVariation(tile, "_RotateLeft");
             //   tile.excludedNeighbours.Add(tileRotated);
                RotateBorders270(tile, tileRotated);
                tileRotated.rotation = new Vector3(0f, 270f, 0f);
                newTiles.Add(tileRotated);
            }
        }

        if (newTiles.Count != 0)
        {
            Tile3D[] aux = tileArray.Concat(newTiles.ToArray()).ToArray();
            tileArray = aux;
        }
    }

    
    void RotateBorders90(Tile3D originalTile, Tile3D tileRotated)
    {
        tileRotated.rightSocket = originalTile.upSocket;
        tileRotated.leftSocket = originalTile.downSocket;
        tileRotated.upSocket = originalTile.leftSocket;
        tileRotated.downSocket = originalTile.rightSocket;

        tileRotated.aboveSocket = originalTile.aboveSocket;
        tileRotated.aboveSocket.rotationIndex = 90;
        tileRotated.belowSocket = originalTile.belowSocket;
        tileRotated.belowSocket.rotationIndex = 90;
        
        //excluded neighbours
        tileRotated.excludedNeighboursRight = originalTile.excludedNeighboursUp;
        tileRotated.excludedNeighboursLeft = originalTile.excludedNeighboursDown;
        tileRotated.excludedNeighboursUp = originalTile.excludedNeighboursLeft;
        tileRotated.excludedNeighboursDown = originalTile.excludedNeighboursRight;
        
       

    }

    void RotateBorders180(Tile3D originalTile, Tile3D tileRotated)
    {
        tileRotated.rightSocket = originalTile.leftSocket;
        tileRotated.leftSocket = originalTile.rightSocket;
        tileRotated.upSocket = originalTile.downSocket;
        tileRotated.downSocket = originalTile.upSocket;
        tileRotated.aboveSocket = originalTile.aboveSocket;
        tileRotated.aboveSocket.rotationIndex = 180;
        tileRotated.belowSocket = originalTile.belowSocket;
        tileRotated.belowSocket.rotationIndex = 180;
        
        //excluded neighbours
        tileRotated.excludedNeighboursLeft = originalTile.excludedNeighboursRight;
        tileRotated.excludedNeighboursRight = originalTile.excludedNeighboursLeft;
        tileRotated.excludedNeighboursUp = originalTile.excludedNeighboursDown;
        tileRotated.excludedNeighboursDown = originalTile.excludedNeighboursUp;

    }

    void RotateBorders270(Tile3D originalTile, Tile3D tileRotated) //O rotar a la izquierda
    {
        tileRotated.rightSocket = originalTile.downSocket;
        tileRotated.leftSocket = originalTile.upSocket;
        tileRotated.upSocket = originalTile.rightSocket;
        tileRotated.downSocket = originalTile.leftSocket;
        tileRotated.aboveSocket = originalTile.aboveSocket;
        tileRotated.aboveSocket.rotationIndex = 270;
        tileRotated.belowSocket = originalTile.belowSocket;
        tileRotated.belowSocket.rotationIndex = 270;
        
        //excluded neighbours
        tileRotated.excludedNeighboursRight = originalTile.excludedNeighboursDown;
        tileRotated.excludedNeighboursLeft = originalTile.excludedNeighboursUp;
        tileRotated.excludedNeighboursUp = originalTile.excludedNeighboursRight;
        tileRotated.excludedNeighboursDown = originalTile.excludedNeighboursLeft;
    }


    //Define the neighbours
    public void DefineNeighbourTiles(ref Tile3D[] tileArray, ref Tile3D[] otherTileArray)
    {
        foreach (Tile3D tile in tileArray)
        {
            foreach (Tile3D otherTile in otherTileArray)
            {
                //HORIZONTAL FACES: Mismo socket y ser simetricos O uno flip y el otro no
                //También se comprueba que la lista de excluidos de cada cara no incluya la otra tile, ni la otra tile a nosotros

                    //Vecinos de arriba
                    if (otherTile.downSocket.socket_name == tile.upSocket.socket_name && !tile.excludedNeighboursUp.Contains(otherTile.tileType) && !otherTile.excludedNeighboursDown.Contains(tile.tileType))
                    {
                        if(tile.upSocket.isSymmetric || otherTile.downSocket.isSymmetric || (otherTile.downSocket.isFlipped && !tile.upSocket.isFlipped) || (!otherTile.downSocket.isFlipped && tile.upSocket.isFlipped))
                        tile.upNeighbours.Add(otherTile);
                    }
                    //Vecinos de abajo
                    if (otherTile.upSocket.socket_name == tile.downSocket.socket_name && !tile.excludedNeighboursDown.Contains(otherTile.tileType) && !otherTile.excludedNeighboursUp.Contains(tile.tileType))
                    {
                        if (otherTile.upSocket.isSymmetric || tile.downSocket.isSymmetric || (otherTile.upSocket.isFlipped && !tile.downSocket.isFlipped) || (!otherTile.upSocket.isFlipped && tile.downSocket.isFlipped))
                        tile.downNeighbours.Add(otherTile);
                    }
                    //Vecinos a la derecha
                    if (otherTile.leftSocket.socket_name == tile.rightSocket.socket_name  && !tile.excludedNeighboursRight.Contains(otherTile.tileType) && !otherTile.excludedNeighboursLeft.Contains(tile.tileType))
                    {
                        if (otherTile.leftSocket.isSymmetric || tile.rightSocket.isSymmetric || (otherTile.leftSocket.isFlipped && !tile.rightSocket.isFlipped) || (!otherTile.leftSocket.isFlipped && tile.rightSocket.isFlipped))
                        tile.rightNeighbours.Add(otherTile);
                    }
                    //Vecinos a la izquierda
                    if (otherTile.rightSocket.socket_name == tile.leftSocket.socket_name && !tile.excludedNeighboursLeft.Contains(otherTile.tileType) && !otherTile.excludedNeighboursRight.Contains(tile.tileType))
                    {
                        if (otherTile.rightSocket.isSymmetric || tile.leftSocket.isSymmetric || (otherTile.rightSocket.isFlipped && !tile.leftSocket.isFlipped) || (!otherTile.rightSocket.isFlipped && tile.leftSocket.isFlipped))
                        tile.leftNeighbours.Add(otherTile);
                    }

                    //VERTICAL FACES: Ambos deben ser rotacionalmente invariables O ambos deben tener el mismo indice de rotacion

                    //Vecinos debajo
                    if (otherTile.belowSocket.socket_name == tile.aboveSocket.socket_name)
                    {
                        if((otherTile.belowSocket.rotationallyInvariant || tile.aboveSocket.rotationallyInvariant) || (otherTile.belowSocket.rotationIndex == tile.aboveSocket.rotationIndex))
                        tile.aboveNeighbours.Add(otherTile);
                    }

                    //Vecinos encima
                    if (otherTile.aboveSocket.socket_name == tile.belowSocket.socket_name)
                    {
                        if ((otherTile.aboveSocket.rotationallyInvariant || tile.belowSocket.rotationallyInvariant) || (otherTile.aboveSocket.rotationIndex == tile.belowSocket.rotationIndex))
                        tile.belowNeighbours.Add(otherTile);
                    }
            }
        }
    }


    // AC-4 Data Structures
    bool[,] possible;
    int[,,] enablerCount;
    int[] remainingOptions;
    Stack<(int cell, int tileIndex)> removals;

    void InitializeGrid()
    {
        for (int y = 0; y < dimensionsY; y++)
        {
            for (int z = 0; z < dimensionsZ; z++)
            {
                for (int x = 0; x < dimensionsX; x++)
                {
                    Cell3D newCell = Instantiate(cellObj, new Vector3(x*cellSize, y * cellSize, z*cellSize), Quaternion.identity, gameObject.transform);
                    //newCell.CreateCell(false, tileObjects, x + (z * dimensionsX) + (y * dimensionsX * dimensionsZ));
                    gridComponents.Add(newCell);
                }
            }
        }
        InitializeAC4();
    }

    void InitializeAC4()
    {
        int numCells = gridComponents.Count;
        int numTiles = tileObjects.Length;
        possible = new bool[numCells, numTiles];
        enablerCount = new int[numCells, numTiles, 6];
        remainingOptions = new int[numCells];
        removals = new Stack<(int, int)>();

        int[,] initialEnabler = new int[numTiles, 6];
        for (int t = 0; t < numTiles; t++)
        {
            initialEnabler[t, 0] = tileObjects[t].upNeighbours.Count;
            initialEnabler[t, 1] = tileObjects[t].downNeighbours.Count;
            initialEnabler[t, 2] = tileObjects[t].rightNeighbours.Count;
            initialEnabler[t, 3] = tileObjects[t].leftNeighbours.Count;
            initialEnabler[t, 4] = tileObjects[t].aboveNeighbours.Count;
            initialEnabler[t, 5] = tileObjects[t].belowNeighbours.Count;
        }

        for (int c = 0; c < numCells; c++)
        {
            remainingOptions[c] = numTiles;
            for (int t = 0; t < numTiles; t++)
            {
                possible[c, t] = true;
                for (int d = 0; d < 6; d++)
                {
                    enablerCount[c, t, d] = initialEnabler[t, d];
                }
            }
        }
    }

    void Ban(int c, int tIndex)
    {
        if (!possible[c, tIndex]) return;
        possible[c, tIndex] = false;
        remainingOptions[c]--;
        removals.Push((c, tIndex));
    }

    List<Tile3D> GetValidNeighbors(Tile3D t, int d)
    {
        if (d == 0) return t.upNeighbours; // Z+
        if (d == 1) return t.downNeighbours; // Z-
        if (d == 2) return t.rightNeighbours; // X+
        if (d == 3) return t.leftNeighbours; // X-
        if (d == 4) return t.aboveNeighbours; // Y+
        if (d == 5) return t.belowNeighbours; // Y-
        return null;
    }

    int OppositeDirection(int d)
    {
        if (d == 0) return 1;
        if (d == 1) return 0;
        if (d == 2) return 3;
        if (d == 3) return 2;
        if (d == 4) return 5;
        if (d == 5) return 4;
        return 0;
    }

    int GetNeighbor(int c, int d)
    {
        int x = c % dimensionsX;
        int y = c / (dimensionsX * dimensionsZ);
        int z = (c / dimensionsX) % dimensionsZ;

        if (d == 0 && z < dimensionsZ - 1) return c + dimensionsX;
        if (d == 1 && z > 0) return c - dimensionsX;
        if (d == 2 && x < dimensionsX - 1) return c + 1;
        if (d == 3 && x > 0) return c - 1;
        if (d == 4 && y < dimensionsY - 1) return c + (dimensionsX * dimensionsZ);
        if (d == 5 && y > 0) return c - (dimensionsX * dimensionsZ);
        return -1;
    }

    void Propagate()
    {
        while (removals.Count > 0)
        {
            var (c, tIndex) = removals.Pop();
            Tile3D t = tileObjects[tIndex];

            for (int d = 0; d < 6; d++)
            {
                int neighbor = GetNeighbor(c, d);
                if (neighbor == -1) continue;

                int opp = OppositeDirection(d);
                List<Tile3D> compatibleInNeighbor = GetValidNeighbors(t, d);
                
                foreach (Tile3D compTile in compatibleInNeighbor)
                {
                    int compIndex = System.Array.IndexOf(tileObjects, compTile);
                    if (compIndex != -1 && possible[neighbor, compIndex])
                    {
                        enablerCount[neighbor, compIndex, opp]--;
                        if (enablerCount[neighbor, compIndex, opp] == 0)
                        {
                            Ban(neighbor, compIndex);
                        }
                    }
                }
            }
        }
    }

    void CollapseTo(int c, Tile3D tile)
    {
        int targetIndex = System.Array.IndexOf(tileObjects, tile);
        for (int t = 0; t < tileObjects.Length; t++)
        {
            if (t != targetIndex && possible[c, t])
            {
                Ban(c, t);
            }
        }
        gridComponents[c].collapsed = true;
        Propagate();
    }

    void InstantiateTile(int c, Tile3D tile)
    {
        Cell3D cell = gridComponents[c];
        //cell.tileOptions = new Tile3D[] { tile };
        
        if (cell.transform.childCount != 0)
        {
            for (int i = cell.transform.childCount - 1; i >= 0; i--)
            {
                DestroyHelper(cell.transform.GetChild(i).gameObject);
            }
        }

        Tile3D instantiatedTile = Instantiate(tile, cell.transform.position, Quaternion.identity, cell.transform);
        if (instantiatedTile.rotation != Vector3.zero)
        {
            instantiatedTile.gameObject.transform.Rotate(tile.rotation, Space.Self);
        }
        instantiatedTile.gameObject.transform.position += instantiatedTile.positionOffset;
        instantiatedTile.gameObject.SetActive(true);
        iterations++;
    }

    void CreateSolidFloor()
    {
        int y = 0;
        for (int z = 0; z < dimensionsZ; z++)
        {
            for (int x = 0; x < dimensionsX; x++)
            {
                int index = x + (z * dimensionsX) + (y * dimensionsX * dimensionsZ);
                CollapseTo(index, floorTile);
                InstantiateTile(index, floorTile);
            }
        }
    }

    void CreateSolidCeiling()
    {
        int y = dimensionsY - 1;
        for (int z = 0; z < dimensionsZ; z++)
        {
            for (int x = 0; x < dimensionsX; x++)
            {
                int index = x + (z * dimensionsX) + (y * dimensionsX * dimensionsZ);
                CollapseTo(index, emptyTile);
                InstantiateTile(index, emptyTile);
            }
        }
    }

    Tile3D ChooseTile(List<(Tile3D tile, int weight)> weightedTiles)
    {
        int totalWeight = weightedTiles.Sum(item => item.weight);
        System.Random random = new System.Random();

        if (totalWeight == 0 && weightedTiles.Count > 0)
        {
            return weightedTiles[random.Next(0, weightedTiles.Count)].tile;
        }

        int randomNumber = random.Next(0, totalWeight);
        foreach (var (tile, weight) in weightedTiles)
        {
            if (randomNumber < weight) return tile;
            randomNumber -= weight;
        }
        return null; 
    }

    void UpdateGeneration()
    {
        while (true)
        {
            int minOptions = int.MaxValue;
            List<int> bestCells = new List<int>();

            for (int c = 0; c < gridComponents.Count; c++)
            {
                if (!gridComponents[c].collapsed)
                {
                    if (remainingOptions[c] < minOptions)
                    {
                        minOptions = remainingOptions[c];
                        bestCells.Clear();
                        bestCells.Add(c);
                    }
                    else if (remainingOptions[c] == minOptions)
                    {
                        bestCells.Add(c);
                    }
                }
            }

            if (bestCells.Count == 0)
            {
                stopwatch.Stop();
                print($"Map generated completely in {stopwatch.ElapsedMilliseconds} ms ({stopwatch.ElapsedMilliseconds / 1000f} s)");
                break;
            }

            if (minOptions == 0)
            {
                Debug.LogError("INCOMPATIBILITY!");
                Regenerate();
                return;
            }

            int cellIndex = bestCells[UnityEngine.Random.Range(0, bestCells.Count)];
            
            List<(Tile3D tile, int weight)> validTiles = new List<(Tile3D, int)>();
            for (int t = 0; t < tileObjects.Length; t++)
            {
                if (possible[cellIndex, t])
                {
                    validTiles.Add((tileObjects[t], tileObjects[t].probability));
                }
            }

            Tile3D selectedTile = ChooseTile(validTiles);
            if (selectedTile == null)
            {
                Debug.LogError("INCOMPATIBILITY!");
                Regenerate();
                return;
            }

            CollapseTo(cellIndex, selectedTile);
            InstantiateTile(cellIndex, selectedTile);
        }
    }

    public void ClearGeneration()
    {
        for (int i = gameObject.transform.childCount - 1; i >= 0; i--)
        {
            DestroyHelper(gameObject.transform.GetChild(i).gameObject);
        }
        
        // Also destroy rotated tiles created dynamically during generation if they exist
        // CreateRemainingCells creates them at scene root with HideFlags.HideInHierarchy
        // We can find them if needed, but since they have HideFlags, GameObject.Find might not get them.
        // If we want to be safe, we can just rely on the user passing the scriptable object.
        // Actually, dynamically instantiated GameObjects need to be cleaned up!
        if (tileObjects != null)
        {
            foreach (Tile3D tile in tileObjects)
            {
                if (tile != null && tile.gameObject.scene.name != null && !PrefabUtility.IsPartOfAnyPrefab(tile.gameObject))
                {
                    DestroyHelper(tile.gameObject);
                }
            }
        }

        iterations = 0;
        gridComponents.Clear();
    }

    public void Regenerate()
    {
        if (onRegenerate != null)
        {
            onRegenerate();
        }
        
        ClearGeneration();

        stopwatch.Reset();
        stopwatch.Start();

        InitializeGrid();
        CreateSolidFloor();
        CreateSolidCeiling();
        UpdateGeneration();
    }
}
