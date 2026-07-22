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

public class WaveFunction3DBitmaskAC3 : MonoBehaviour
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
    [SerializeField] private Tileset tileset;                       // Tileset ScriptableObject (shared with GPU version)
    [SerializeField] private Cell3D cellObj;                        // Cell prefab containing collapse logic
    #endregion

    #region Internal State
    private Tile3D[] tileObjects;                                   // Full palette of available tiles (including dynamic rotations)
    private ulong[][] validNeighbors;                               // Precomputed bitmasks for instant adjacency lookups
    private int cellSize;
    private int iterations = 0;
    private List<Cell3D> gridComponents;
    private Stopwatch stopwatch;
    
    // AC-3 Constraint Propagation Queue
    private Queue<int> propagationQueue;
    private bool[] inQueue;
    
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
        PrecomputeBitmasks();

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
        print($"AC3 Map generated completely in {stopwatch.ElapsedMilliseconds} ms ({stopwatch.ElapsedMilliseconds / 1000f} s)");
    }

    private bool RunGenerationLoop()
    {
        int totalCells = dimensionsX * dimensionsZ * dimensionsY;
        
        // Initial AC-3 propagation from Floor and Ceiling constraints
        if (!PropagateConstraints()) return false;

        while (iterations < totalCells)
        {
            // Find the cell with the lowest entropy and collapse it. 
            if (!CheckEntropy())
            {
                return false; // Incompatibility hit during collapse selection
            }
            
            // AC-3: Propagate constraints originating from the newly collapsed cell
            if (!PropagateConstraints())
            {
                return false; // Incompatibility hit during propagation
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

    #region WFC AC-3 Constraint Logic (Bitmasks)
    
    private void EnqueueCell(int index)
    {
        if (!inQueue[index])
        {
            propagationQueue.Enqueue(index);
            inQueue[index] = true;
        }
    }

    private void EnqueueNeighbors(int index)
    {
        int z = (index / dimensionsX) % dimensionsZ;
        int y = index / (dimensionsX * dimensionsZ);
        int x = index % dimensionsX;

        int up = x + ((z + 1) * dimensionsX) + (y * dimensionsX * dimensionsZ);
        int down = x + ((z - 1) * dimensionsX) + (y * dimensionsX * dimensionsZ);
        int right = (x + 1) + (z * dimensionsX) + (y * dimensionsX * dimensionsZ);
        int left = (x - 1) + (z * dimensionsX) + (y * dimensionsX * dimensionsZ);
        int above = x + (z * dimensionsX) + ((y + 1) * dimensionsX * dimensionsZ);
        int below = x + (z * dimensionsX) + ((y - 1) * dimensionsX * dimensionsZ);

        if (z < dimensionsZ - 1) EnqueueCell(up);
        if (z > 0) EnqueueCell(down);
        if (x < dimensionsX - 1) EnqueueCell(right);
        if (x > 0) EnqueueCell(left);
        if (y < dimensionsY - 1) EnqueueCell(above);
        if (y > 0) EnqueueCell(below);
    }

    private bool PropagateConstraints()
    {
        // AC-3 Algorithm: Process the queue until empty.
        // Only re-evaluates cells that have had a neighbor's domain reduced.
        while (propagationQueue.Count > 0)
        {
            int index = propagationQueue.Dequeue();
            inQueue[index] = false;
            
            if (gridComponents[index].collapsed) continue;

            int z = (index / dimensionsX) % dimensionsZ;
            int y = index / (dimensionsX * dimensionsZ);
            int x = index % dimensionsX;

            bool changed = CheckNeighbours(x, y, z, index);
            
            if (gridComponents[index].entropy == 0)
            {
                return false; // Dead-end (incompatibility)
            }
            
            if (changed)
            {
                // If this cell's domain was reduced, its neighbors must be re-evaluated
                EnqueueNeighbors(index);
            }
        }
        return true;
    }

    private void PrecomputeBitmasks()
    {
        // 0 = Up (Z+), 1 = Down (Z-), 2 = Right (X+), 3 = Left (X-), 4 = Above (Y+), 5 = Below (Y-)
        validNeighbors = new ulong[6][];
        for (int i = 0; i < 6; i++) validNeighbors[i] = new ulong[tileObjects.Length];

        for (int i = 0; i < tileObjects.Length; i++)
        {
            Tile3D tile = tileObjects[i];
            
            foreach (var n in tile.upNeighbours) 
            {
                int index = Array.IndexOf(tileObjects, n);
                if (index != -1) validNeighbors[0][i] |= (1UL << index);
            }
            foreach (var n in tile.downNeighbours)
            {
                int index = Array.IndexOf(tileObjects, n);
                if (index != -1) validNeighbors[1][i] |= (1UL << index);
            }
            foreach (var n in tile.rightNeighbours)
            {
                int index = Array.IndexOf(tileObjects, n);
                if (index != -1) validNeighbors[2][i] |= (1UL << index);
            }
            foreach (var n in tile.leftNeighbours)
            {
                int index = Array.IndexOf(tileObjects, n);
                if (index != -1) validNeighbors[3][i] |= (1UL << index);
            }
            foreach (var n in tile.aboveNeighbours)
            {
                int index = Array.IndexOf(tileObjects, n);
                if (index != -1) validNeighbors[4][i] |= (1UL << index);
            }
            foreach (var n in tile.belowNeighbours)
            {
                int index = Array.IndexOf(tileObjects, n);
                if (index != -1) validNeighbors[5][i] |= (1UL << index);
            }
        }
    }

    bool CheckNeighbours(int x, int y, int z, int index)
    {
        int up, down, left, right, above, below;
        
        right = (x + 1) + (z * dimensionsX) + (y * dimensionsX * dimensionsZ);
        left = (x - 1) + (z * dimensionsX) + (y * dimensionsX * dimensionsZ);
        up = x + ((z + 1) * dimensionsX) + (y * dimensionsX * dimensionsZ);
        down = x + ((z - 1) * dimensionsX) + (y * dimensionsX * dimensionsZ);
        above = x + (z * dimensionsX) + ((y + 1) * dimensionsX * dimensionsZ);
        below = x + (z * dimensionsX) + ((y - 1) * dimensionsX * dimensionsZ);

        ulong oldMask = gridComponents[index].possibleTilesMask;
        ulong currentMask = oldMask;
        
        if (z > 0)
        {
            ulong allowed = 0;
            ulong neighborMask = gridComponents[down].possibleTilesMask;
            for (int i = 0; i < tileObjects.Length; i++) {
                if ((neighborMask & (1UL << i)) != 0) allowed |= validNeighbors[0][i];
            }
            currentMask &= allowed;
        }

        if (x < dimensionsX - 1)
        {
            ulong allowed = 0;
            ulong neighborMask = gridComponents[right].possibleTilesMask;
            for (int i = 0; i < tileObjects.Length; i++) {
                if ((neighborMask & (1UL << i)) != 0) allowed |= validNeighbors[3][i];
            }
            currentMask &= allowed;
        }

        if (z < dimensionsZ - 1)
        {
            ulong allowed = 0;
            ulong neighborMask = gridComponents[up].possibleTilesMask;
            for (int i = 0; i < tileObjects.Length; i++) {
                if ((neighborMask & (1UL << i)) != 0) allowed |= validNeighbors[1][i];
            }
            currentMask &= allowed;
        }

        if (x > 0)
        {
            ulong allowed = 0;
            ulong neighborMask = gridComponents[left].possibleTilesMask;
            for (int i = 0; i < tileObjects.Length; i++) {
                if ((neighborMask & (1UL << i)) != 0) allowed |= validNeighbors[2][i];
            }
            currentMask &= allowed;
        }

        if (y > 0)
        {
            ulong allowed = 0;
            ulong neighborMask = gridComponents[below].possibleTilesMask;
            for (int i = 0; i < tileObjects.Length; i++) {
                if ((neighborMask & (1UL << i)) != 0) allowed |= validNeighbors[4][i];
            }
            currentMask &= allowed;
        }

        if (y < dimensionsY - 1)
        {
            ulong allowed = 0;
            ulong neighborMask = gridComponents[above].possibleTilesMask;
            for (int i = 0; i < tileObjects.Length; i++) {
                if ((neighborMask & (1UL << i)) != 0) allowed |= validNeighbors[5][i];
            }
            currentMask &= allowed;
        }

        if (currentMask != oldMask)
        {
            int newEntropy = CountBits(currentMask);
            gridComponents[index].RecreateCell(currentMask, newEntropy);
            return true;
        }
        
        return false;
    }

    bool CheckEntropy()
    {
        List<Cell3D> tempGrid = new List<Cell3D>(gridComponents);
        tempGrid.RemoveAll(c => c.collapsed);

        if (tempGrid.Count == 0) return true;

        tempGrid.Sort((a, b) => { return a.entropy - b.entropy; });

        int minEntropy = tempGrid[0].entropy;
        int stopIndex = 0;

        for (int i = 1; i < tempGrid.Count; i++)
        {
            if (tempGrid[i].entropy > minEntropy)
            {
                stopIndex = i;
                break;
            }
        }

        if (stopIndex > 0)
        {
            tempGrid.RemoveRange(stopIndex, tempGrid.Count - stopIndex);
        }

        return CollapseCell(tempGrid);
    }

    bool CollapseCell(List<Cell3D> tempGrid)
    {
        Cell3D cellToCollapse = tempGrid[UnityEngine.Random.Range(0, tempGrid.Count)];
        cellToCollapse.collapsed = true;

        List<(Tile3D tile, int weight)> weightedTiles = new List<(Tile3D, int)>();
        for (int i = 0; i < tileObjects.Length; i++)
        {
            if ((cellToCollapse.possibleTilesMask & (1UL << i)) != 0)
            {
                weightedTiles.Add((tileObjects[i], tileObjects[i].probability));
            }
        }
        
        Tile3D selectedTile = ChooseTile(weightedTiles);

        if (selectedTile is null)
        {
            Debug.LogError("INCOMPATIBILITY!");
            return false;
        }
        
        int selectedIndex = Array.IndexOf(tileObjects, selectedTile);
        cellToCollapse.possibleTilesMask = (1UL << selectedIndex);
        cellToCollapse.entropy = 1;
        Tile3D foundTile = selectedTile;

        if (cellToCollapse.transform.childCount != 0)
        {
            foreach (Transform child in cellToCollapse.transform)
            {
                DestroyHelper(child.gameObject);
            }
        }

        Tile3D instantiatedTile = Instantiate(foundTile, cellToCollapse.transform.position, Quaternion.identity, cellToCollapse.transform);
        if (instantiatedTile.rotation != Vector3.zero)
        {
            instantiatedTile.gameObject.transform.Rotate(foundTile.rotation, Space.Self);
        }
        
        instantiatedTile.gameObject.transform.position += instantiatedTile.positionOffset;
        instantiatedTile.gameObject.SetActive(true);

        // Notify neighbors that this cell has collapsed
        EnqueueNeighbors(cellToCollapse.index);

        return true;
    }

    private int CountBits(ulong value)
    {
        int count = 0;
        while (value != 0)
        {
            count++;
            value &= value - 1;
        }
        return count;
    }
    #endregion

    #region Grid Initialization
    void InitializeGrid()
    {
        int totalCells = dimensionsX * dimensionsY * dimensionsZ;
        propagationQueue = new Queue<int>();
        inQueue = new bool[totalCells];
        
        ulong fullMask = (tileObjects.Length == 64) ? ulong.MaxValue : (1UL << tileObjects.Length) - 1;
        for (int y = 0; y < dimensionsY; y++)
        {
            for (int z = 0; z < dimensionsZ; z++)
            {
                for (int x = 0; x < dimensionsX; x++)
                {
                    int index = x + (z * dimensionsX) + (y * dimensionsX * dimensionsZ);
                    Cell3D newCell = Instantiate(cellObj, new Vector3(x*cellSize, y * cellSize, z*cellSize), Quaternion.identity, gameObject.transform);
                    newCell.CreateCell(false, fullMask, tileObjects.Length, index);
                    gridComponents.Add(newCell);
                }
            }
        }
    }

    void CreateSolidFloor()
    {
        int floorIndex = Array.IndexOf(tileObjects, floorTile);
        int y = 0;
        for (int z = 0; z < dimensionsZ; z++)
        {
            for (int x = 0; x < dimensionsX; x++)
            {
                var index = x + (z * dimensionsX) + (y * dimensionsX * dimensionsZ);
                Cell3D cellToCollapse = gridComponents[index];
                cellToCollapse.possibleTilesMask = (1UL << floorIndex);
                cellToCollapse.entropy = 1;
                cellToCollapse.collapsed = true;
                if (cellToCollapse.transform.childCount != 0)
                {
                    foreach (Transform child in cellToCollapse.transform)
                    {
                        DestroyHelper(child.gameObject);
                    }
                }

                Tile3D instantiatedTile = Instantiate(floorTile, cellToCollapse.transform.position, Quaternion.identity, cellToCollapse.transform);
                if (instantiatedTile.rotation != Vector3.zero)
                {
                    instantiatedTile.gameObject.transform.Rotate(floorTile.rotation, Space.Self);
                }

                instantiatedTile.gameObject.transform.position += instantiatedTile.positionOffset;
                instantiatedTile.gameObject.SetActive(true);
                
                iterations++;
                EnqueueNeighbors(index); // Propagate constraints from floor
            }
        }
    }

    void CreateSolidCeiling()
    {
        int emptyIndex = Array.IndexOf(tileObjects, emptyTile);
        int y = dimensionsY-1;
        for (int z = 0; z < dimensionsZ; z++)
        {
            for (int x = 0; x < dimensionsX; x++)
            {
                var index = x + (z * dimensionsX) + (y * dimensionsX * dimensionsZ);
                Cell3D cellToCollapse = gridComponents[index];
                cellToCollapse.possibleTilesMask = (1UL << emptyIndex);
                cellToCollapse.entropy = 1;
                cellToCollapse.collapsed = true;
                if (cellToCollapse.transform.childCount != 0)
                {
                    foreach (Transform child in cellToCollapse.transform)
                    {
                        DestroyHelper(child.gameObject);
                    }
                }

                Tile3D instantiatedTile = Instantiate(emptyTile, cellToCollapse.transform.position, Quaternion.identity, cellToCollapse.transform);
                if (instantiatedTile.rotation != Vector3.zero)
                {
                    instantiatedTile.gameObject.transform.Rotate(floorTile.rotation, Space.Self);
                }

                instantiatedTile.gameObject.transform.position += instantiatedTile.positionOffset;
                instantiatedTile.gameObject.SetActive(true);
                
                iterations++;
                EnqueueNeighbors(index); // Propagate constraints from ceiling
            }
        }
    }
    #endregion

    #region Tileset Generation & Socket Matching
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
