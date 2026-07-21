using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
#if UNITY_EDITOR
using UnityEditor;
#endif

using Tile3D = WFC3DMapGenerator.Tile3D;
using Tileset = WFC3DMapGenerator.Tileset;

public class WaveFunction3DAC4 : MonoBehaviour
{
    #region Configuration & Inspector
    [Header("Grid Dimensions")]
    [SerializeField] private int dimensionsX;
    [SerializeField] private int dimensionsZ;
    [SerializeField] private int dimensionsY;

    [Header("Core Tiles")]
    [SerializeField] private Tile3D floorTile;
    [SerializeField] private Tile3D emptyTile;
    
    [Header("Assets & Prefabs")]
    [SerializeField] private Tileset tileset;                       // Tileset ScriptableObject
    [SerializeField] private Cell3D cellObj;                        // Cell prefab containing collapse logic
    #endregion

    #region Internal State
    private Tile3D[] tileObjects;                                   
    private int cellSize;
    private int iterations = 0;
    private List<Cell3D> gridComponents;
    private Stopwatch stopwatch;
    
    // AC-4 Specific Structures (List/Array Based)
    private bool[,] possible;
    private int[,,] enablerCount; // [cell, tileIndex, direction]
    private int[] remainingOptions;
    private Stack<(int cell, int tileIndex)> removals;
    
    public delegate void OnRegenerate();
    public static event OnRegenerate onRegenerate;
    #endregion

    #region Unity Lifecycle
    void Awake()
    {
        if (Application.isPlaying) StartGeneration();
    }
    #endregion

    #region Main Generation Flow
    public void StartGeneration()
    {
        ClearGeneration();

        tileObjects = tileset.tiles.ToArray();
        cellSize = (int)tileset.tileSize;

        ClearNeighbours(ref tileObjects);
        CreateRemainingCells(ref tileObjects);
        DefineNeighbourTiles(ref tileObjects, ref tileObjects);

        gridComponents = new List<Cell3D>();

        stopwatch = new Stopwatch();
        stopwatch.Start();
        
        GenerateMap();
    }

    private void GenerateMap()
    {
        bool success = false;
        
        while (!success)
        {
            ResetGrid(); 
            
            gridComponents = new List<Cell3D>();
            InitializeGrid();
            CreateSolidFloor();
            CreateSolidCeiling();
            
            success = RunGenerationLoop();
        }
        
        stopwatch.Stop();
        print($"AC4 List-Based Map generated completely in {stopwatch.ElapsedMilliseconds} ms ({stopwatch.ElapsedMilliseconds / 1000f} s)");
    }

    private bool RunGenerationLoop()
    {
        int totalCells = dimensionsX * dimensionsZ * dimensionsY;
        
        // Initial AC-4 propagation from Floor and Ceiling constraints
        Propagate();

        while (iterations < totalCells)
        {
            if (!CheckEntropy())
            {
                return false; // Incompatibility hit
            }
            iterations++;
        }
        return true;
    }
    
    public void Regenerate()
    {
        if (onRegenerate != null) onRegenerate();
        
        stopwatch = new Stopwatch();
        stopwatch.Start();

        GenerateMap();
    }
    #endregion

    #region WFC Constraint Logic (AC-4 List-Based)

    int GetNeighbor(int c, int d)
    {
        int z = (c / dimensionsX) % dimensionsZ;
        int y = c / (dimensionsX * dimensionsZ);
        int x = c % dimensionsX;

        // 0 = Up (Z+), 1 = Down (Z-), 2 = Right (X+), 3 = Left (X-), 4 = Above (Y+), 5 = Below (Y-)
        if (d == 0 && z < dimensionsZ - 1) return c + dimensionsX;
        if (d == 1 && z > 0) return c - dimensionsX;
        if (d == 2 && x < dimensionsX - 1) return c + 1;
        if (d == 3 && x > 0) return c - 1;
        if (d == 4 && y < dimensionsY - 1) return c + (dimensionsX * dimensionsZ);
        if (d == 5 && y > 0) return c - (dimensionsX * dimensionsZ);
        return -1;
    }

    int OppositeDirection(int d)
    {
        if (d == 0) return 1;
        if (d == 1) return 0;
        if (d == 2) return 3;
        if (d == 3) return 2;
        if (d == 4) return 5;
        if (d == 5) return 4;
        return -1;
    }

    List<Tile3D> GetValidNeighbors(Tile3D tile, int d)
    {
        if (d == 0) return tile.upNeighbours;
        if (d == 1) return tile.downNeighbours;
        if (d == 2) return tile.rightNeighbours;
        if (d == 3) return tile.leftNeighbours;
        if (d == 4) return tile.aboveNeighbours;
        if (d == 5) return tile.belowNeighbours;
        return new List<Tile3D>();
    }

    void Ban(int c, int t)
    {
        if (possible[c, t])
        {
            possible[c, t] = false;
            remainingOptions[c]--;
            removals.Push((c, t));
        }
    }

    void Propagate()
    {
        while (removals.Count > 0)
        {
            var (c, tIndex) = removals.Pop();
            Tile3D t = tileObjects[tIndex];

            // When tile t is removed from cell c, it can no longer act as an enabler 
            // for compatible tiles in neighboring cells.
            for (int d = 0; d < 6; d++)
            {
                int neighbor = GetNeighbor(c, d);
                if (neighbor == -1) continue;

                int opp = OppositeDirection(d);
                
                // Which tiles in the neighbor cell were supported by this removed tile?
                List<Tile3D> compatibleInNeighbor = GetValidNeighbors(t, d);
                
                foreach (Tile3D compTile in compatibleInNeighbor)
                {
                    int compIndex = Array.IndexOf(tileObjects, compTile);
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
        int targetIndex = Array.IndexOf(tileObjects, tile);
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

        // Update the visual representation options array to reflect standard non-bitmask Cell3D expectations
        cell.RecreateCell(new Tile3D[] { tile });
    }

    bool CheckEntropy()
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

        if (bestCells.Count == 0) return true; // All collapsed

        if (minOptions == 0)
        {
            Debug.LogError("INCOMPATIBILITY!");
            return false;
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
            return false;
        }

        CollapseTo(cellIndex, selectedTile);
        InstantiateTile(cellIndex, selectedTile);
        
        return true;
    }

    #endregion

    #region Grid Initialization
    void InitializeGrid()
    {
        int totalCells = dimensionsX * dimensionsY * dimensionsZ;
        removals = new Stack<(int cell, int tileIndex)>();
        
        possible = new bool[totalCells, tileObjects.Length];
        enablerCount = new int[totalCells, tileObjects.Length, 6];
        remainingOptions = new int[totalCells];

        // Precompute initial enablers globally
        int[,] initialEnabler = new int[tileObjects.Length, 6];
        for (int t = 0; t < tileObjects.Length; t++)
        {
            initialEnabler[t, 0] = tileObjects[t].upNeighbours.Count;
            initialEnabler[t, 1] = tileObjects[t].downNeighbours.Count;
            initialEnabler[t, 2] = tileObjects[t].rightNeighbours.Count;
            initialEnabler[t, 3] = tileObjects[t].leftNeighbours.Count;
            initialEnabler[t, 4] = tileObjects[t].aboveNeighbours.Count;
            initialEnabler[t, 5] = tileObjects[t].belowNeighbours.Count;
        }

        for (int y = 0; y < dimensionsY; y++)
        {
            for (int z = 0; z < dimensionsZ; z++)
            {
                for (int x = 0; x < dimensionsX; x++)
                {
                    int index = x + (z * dimensionsX) + (y * dimensionsX * dimensionsZ);
                    Cell3D newCell = Instantiate(cellObj, new Vector3(x*cellSize, y * cellSize, z*cellSize), Quaternion.identity, gameObject.transform);
                    newCell.CreateCell(false, tileObjects, index);
                    gridComponents.Add(newCell);
                }
            }
        }

        for (int c = 0; c < totalCells; c++)
        {
            remainingOptions[c] = tileObjects.Length;
            for (int t = 0; t < tileObjects.Length; t++)
            {
                possible[c, t] = true;
                for (int d = 0; d < 6; d++)
                {
                    enablerCount[c, t, d] = initialEnabler[t, d];
                }
            }
        }
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
        int y = dimensionsY-1;
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
    #endregion

    #region Palette Generation & Socket Matching
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

    private void CreateRemainingCells(ref Tile3D[] tileArray)
    {
        List<Tile3D> newTiles = new List<Tile3D>();
        foreach (Tile3D tile in tileArray)
        {
            if (tile.rotateRight) 
            {
                Tile3D tileRotated = CreateNewTileVariation(tile, "_RotateRight");
                RotateBorders90(tile, tileRotated);
                tileRotated.rotation = new Vector3(0f, 90f, 0f);
                newTiles.Add(tileRotated);
            }

            if (tile.rotate180)
            {
                Tile3D tileRotated = CreateNewTileVariation(tile, "_Rotate180");
                RotateBorders180(tile, tileRotated);
                tileRotated.rotation = new Vector3(0f, 180f, 0f);
                newTiles.Add(tileRotated);
            }

            if (tile.rotateLeft)
            {
                Tile3D tileRotated = CreateNewTileVariation(tile, "_RotateLeft");
                RotateBorders270(tile, tileRotated);
                tileRotated.rotation = new Vector3(0f, 270f, 0f);
                newTiles.Add(tileRotated);
            }
        }

        if (newTiles.Count != 0)
        {
            tileArray = tileArray.Concat(newTiles).ToArray();
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
        
        tileRotated.excludedNeighboursLeft = originalTile.excludedNeighboursRight;
        tileRotated.excludedNeighboursRight = originalTile.excludedNeighboursLeft;
        tileRotated.excludedNeighboursUp = originalTile.excludedNeighboursDown;
        tileRotated.excludedNeighboursDown = originalTile.excludedNeighboursUp;
    }

    void RotateBorders270(Tile3D originalTile, Tile3D tileRotated) 
    {
        tileRotated.rightSocket = originalTile.downSocket;
        tileRotated.leftSocket = originalTile.upSocket;
        tileRotated.upSocket = originalTile.rightSocket;
        tileRotated.downSocket = originalTile.leftSocket;
        
        tileRotated.aboveSocket = originalTile.aboveSocket;
        tileRotated.aboveSocket.rotationIndex = 270;
        tileRotated.belowSocket = originalTile.belowSocket;
        tileRotated.belowSocket.rotationIndex = 270;
        
        tileRotated.excludedNeighboursRight = originalTile.excludedNeighboursDown;
        tileRotated.excludedNeighboursLeft = originalTile.excludedNeighboursUp;
        tileRotated.excludedNeighboursUp = originalTile.excludedNeighboursRight;
        tileRotated.excludedNeighboursDown = originalTile.excludedNeighboursLeft;
    }

    public void DefineNeighbourTiles(ref Tile3D[] tileArray, ref Tile3D[] otherTileArray)
    {
        foreach (Tile3D tile in tileArray)
        {
            foreach (Tile3D otherTile in otherTileArray)
            {
                if (otherTile.downSocket.socket_name == tile.upSocket.socket_name && !tile.excludedNeighboursUp.Contains(otherTile.tileType) && !otherTile.excludedNeighboursDown.Contains(tile.tileType))
                {
                    if(tile.upSocket.isSymmetric || otherTile.downSocket.isSymmetric || (otherTile.downSocket.isFlipped && !tile.upSocket.isFlipped) || (!otherTile.downSocket.isFlipped && tile.upSocket.isFlipped))
                        tile.upNeighbours.Add(otherTile);
                }
                
                if (otherTile.upSocket.socket_name == tile.downSocket.socket_name && !tile.excludedNeighboursDown.Contains(otherTile.tileType) && !otherTile.excludedNeighboursUp.Contains(tile.tileType))
                {
                    if (otherTile.upSocket.isSymmetric || tile.downSocket.isSymmetric || (otherTile.upSocket.isFlipped && !tile.downSocket.isFlipped) || (!otherTile.upSocket.isFlipped && tile.downSocket.isFlipped))
                        tile.downNeighbours.Add(otherTile);
                }
                
                if (otherTile.leftSocket.socket_name == tile.rightSocket.socket_name  && !tile.excludedNeighboursRight.Contains(otherTile.tileType) && !otherTile.excludedNeighboursLeft.Contains(tile.tileType))
                {
                    if (otherTile.leftSocket.isSymmetric || tile.rightSocket.isSymmetric || (otherTile.leftSocket.isFlipped && !tile.rightSocket.isFlipped) || (!otherTile.leftSocket.isFlipped && tile.rightSocket.isFlipped))
                        tile.rightNeighbours.Add(otherTile);
                }
                
                if (otherTile.rightSocket.socket_name == tile.leftSocket.socket_name && !tile.excludedNeighboursLeft.Contains(otherTile.tileType) && !otherTile.excludedNeighboursRight.Contains(tile.tileType))
                {
                    if (otherTile.rightSocket.isSymmetric || tile.leftSocket.isSymmetric || (otherTile.rightSocket.isFlipped && !tile.leftSocket.isFlipped) || (!otherTile.rightSocket.isFlipped && tile.leftSocket.isFlipped))
                        tile.leftNeighbours.Add(otherTile);
                }

                if (otherTile.belowSocket.socket_name == tile.aboveSocket.socket_name)
                {
                    if((otherTile.belowSocket.rotationallyInvariant || tile.aboveSocket.rotationallyInvariant) || (otherTile.belowSocket.rotationIndex == tile.aboveSocket.rotationIndex))
                        tile.aboveNeighbours.Add(otherTile);
                }

                if (otherTile.aboveSocket.socket_name == tile.belowSocket.socket_name)
                {
                    if ((otherTile.aboveSocket.rotationallyInvariant || tile.belowSocket.rotationallyInvariant) || (otherTile.aboveSocket.rotationIndex == tile.belowSocket.rotationIndex))
                        tile.belowNeighbours.Add(otherTile);
                }
            }
        }
    }
    #endregion

    #region Helper & Cleanup Methods
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

    void DestroyHelper(GameObject obj)
    {
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    private void ResetGrid()
    {
        for (int i = gameObject.transform.childCount - 1; i >= 0; i--)
        {
            DestroyHelper(gameObject.transform.GetChild(i).gameObject);
        }
        
        iterations = 0;
        if (gridComponents != null)
        {
            gridComponents.Clear();
        }
    }

    public void ClearGeneration()
    {
        ResetGrid();
        
        if (tileObjects != null)
        {
            foreach (Tile3D tile in tileObjects)
            {
                if (tile != null && tile.gameObject != null && tile.gameObject.scene.name != null && !PrefabUtility.IsPartOfAnyPrefab(tile.gameObject))
                {
                    DestroyHelper(tile.gameObject);
                }
            }
            tileObjects = null;
        }
    }
    #endregion
}
