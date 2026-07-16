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

public class WaveFunction3D : MonoBehaviour
{
    #region Configuration & Inspector
    [Header("Grid Dimensions")]
    [SerializeField] private int dimensionsX, dimensionsZ, dimensionsY;
    
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
        // 1. Clean up any leftovers from previous generations
        ClearGeneration();

        // 2. Initialize tile palette from the ScriptableObject
        tileObjects = tileset.tiles.ToArray();
        cellSize = (int)tileset.tileSize;

        // 3. Prepare the adjacency constraints (Palette Generation)
        ClearNeighbours(ref tileObjects);
        CreateRemainingCells(ref tileObjects);
        DefineNeighbourTiles(ref tileObjects, ref tileObjects);
        PrecomputeBitmasks();

        gridComponents = new List<Cell3D>();

        stopwatch = new Stopwatch();
        stopwatch.Start();
        
        // 4. Begin the WFC loop
        GenerateMap();
    }

    private void GenerateMap()
    {
        bool success = false;
        
        // Loop until a valid map is generated without hitting any dead-ends (incompatibilities)
        while (!success)
        {
            ResetGrid(); // Reset only the grid components, keeping the precomputed tile palette intact
            
            gridComponents = new List<Cell3D>();
            InitializeGrid();
            CreateSolidFloor();
            CreateSolidCeiling();
            
            success = RunGenerationLoop();
        }
        
        stopwatch.Stop();
        print($"Map generated completely in {stopwatch.ElapsedMilliseconds} ms ({stopwatch.ElapsedMilliseconds / 1000f} s)");
    }

    private bool RunGenerationLoop()
    {
        int totalCells = dimensionsX * dimensionsZ * dimensionsY;
        
        // Unrolled recursion: We iteratively scan and collapse the grid instead of using recursive function calls.
        // This completely prevents StackOverflowExceptions on large grids (e.g., 16x16x16).
        while (iterations < totalCells)
        {
            // Propagate constraints across the entire grid
            for (int y = 0; y < dimensionsY; y++)
            {
                for (int z = 0; z < dimensionsZ; z++)
                {
                    for (int x = 0; x < dimensionsX; x++)
                    {
                        CheckNeighbours(x, y, z);
                    }
                }
            }

            // Find the cell with the lowest entropy and collapse it. 
            // Returns false if a dead-end (incompatibility) is reached.
            if (!CheckEntropy())
            {
                return false; 
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

    #region WFC Constraint Logic (Bitmasks)
    private void PrecomputeBitmasks()
    {
        // 0 = Up (Z+), 1 = Down (Z-), 2 = Right (X+), 3 = Left (X-), 4 = Above (Y+), 5 = Below (Y-)
        validNeighbors = new ulong[6][];
        for (int i = 0; i < 6; i++) validNeighbors[i] = new ulong[tileObjects.Length];

        // Convert the neighbor lists into fast ulong bitmasks for O(1) constraint propagation
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

    void CheckNeighbours(int x, int y, int z)
    {
        int up, down, left, right, above, below;
        var index = x + (z * dimensionsX) + (y * dimensionsX * dimensionsZ);
        
        if (gridComponents[index].collapsed) return;

        right = (x + 1) + (z * dimensionsX) + (y * dimensionsX * dimensionsZ);
        left = (x - 1) + (z * dimensionsX) + (y * dimensionsX * dimensionsZ);
        up = x + ((z + 1) * dimensionsX) + (y * dimensionsX * dimensionsZ);
        down = x + ((z - 1) * dimensionsX) + (y * dimensionsX * dimensionsZ);
        above = x + (z * dimensionsX) + ((y + 1) * dimensionsX * dimensionsZ);
        below = x + (z * dimensionsX) + ((y - 1) * dimensionsX * dimensionsZ);

        ulong currentMask = gridComponents[index].possibleTilesMask;
        
        // Z- (Down) neighbor restricts what can be placed in this cell
        if (z > 0)
        {
            ulong allowed = 0;
            ulong neighborMask = gridComponents[down].possibleTilesMask;
            for (int i = 0; i < tileObjects.Length; i++) {
                if ((neighborMask & (1UL << i)) != 0) allowed |= validNeighbors[0][i];
            }
            currentMask &= allowed;
        }

        // X+ (Right) neighbor
        if (x < dimensionsX - 1)
        {
            ulong allowed = 0;
            ulong neighborMask = gridComponents[right].possibleTilesMask;
            for (int i = 0; i < tileObjects.Length; i++) {
                if ((neighborMask & (1UL << i)) != 0) allowed |= validNeighbors[3][i];
            }
            currentMask &= allowed;
        }

        // Z+ (Up) neighbor
        if (z < dimensionsZ - 1)
        {
            ulong allowed = 0;
            ulong neighborMask = gridComponents[up].possibleTilesMask;
            for (int i = 0; i < tileObjects.Length; i++) {
                if ((neighborMask & (1UL << i)) != 0) allowed |= validNeighbors[1][i];
            }
            currentMask &= allowed;
        }

        // X- (Left) neighbor
        if (x > 0)
        {
            ulong allowed = 0;
            ulong neighborMask = gridComponents[left].possibleTilesMask;
            for (int i = 0; i < tileObjects.Length; i++) {
                if ((neighborMask & (1UL << i)) != 0) allowed |= validNeighbors[2][i];
            }
            currentMask &= allowed;
        }

        // Y- (Below) neighbor
        if (y > 0)
        {
            ulong allowed = 0;
            ulong neighborMask = gridComponents[below].possibleTilesMask;
            for (int i = 0; i < tileObjects.Length; i++) {
                if ((neighborMask & (1UL << i)) != 0) allowed |= validNeighbors[4][i];
            }
            currentMask &= allowed;
        }

        // Y+ (Above) neighbor
        if (y < dimensionsY - 1)
        {
            ulong allowed = 0;
            ulong neighborMask = gridComponents[above].possibleTilesMask;
            for (int i = 0; i < tileObjects.Length; i++) {
                if ((neighborMask & (1UL << i)) != 0) allowed |= validNeighbors[5][i];
            }
            currentMask &= allowed;
        }

        int newEntropy = CountBits(currentMask);
        gridComponents[index].RecreateCell(currentMask, newEntropy);
    }

    bool CheckEntropy()
    {
        List<Cell3D> tempGrid = new List<Cell3D>(gridComponents);
        tempGrid.RemoveAll(c => c.collapsed);

        if (tempGrid.Count == 0) return true; // Generation complete

        // Sort by lowest entropy (fewest possible tiles remaining)
        tempGrid.Sort((a, b) => { return a.entropy - b.entropy; });

        int minEntropy = tempGrid[0].entropy;
        int stopIndex = 0;

        // Keep only the cells that share the lowest entropy
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
        // Randomly pick one of the cells tied for lowest entropy
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

        // Clean up any placeholder visualization children
        if (cellToCollapse.transform.childCount != 0)
        {
            foreach (Transform child in cellToCollapse.transform)
            {
                DestroyHelper(child.gameObject);
            }
        }

        // Instantiate the chosen tile prefab
        Tile3D instantiatedTile = Instantiate(foundTile, cellToCollapse.transform.position, Quaternion.identity, cellToCollapse.transform);
        if (instantiatedTile.rotation != Vector3.zero)
        {
            instantiatedTile.gameObject.transform.Rotate(foundTile.rotation, Space.Self);
        }
        
        instantiatedTile.gameObject.transform.position += instantiatedTile.positionOffset;
        instantiatedTile.gameObject.SetActive(true);

        return true;
    }

    private int CountBits(ulong value)
    {
        // Brian Kernighan's algorithm to count set bits
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
        ulong fullMask = (tileObjects.Length == 64) ? ulong.MaxValue : (1UL << tileObjects.Length) - 1;
        for (int y = 0; y < dimensionsY; y++)
        {
            for (int z = 0; z < dimensionsZ; z++)
            {
                for (int x = 0; x < dimensionsX; x++)
                {
                    Cell3D newCell = Instantiate(cellObj, new Vector3(x*cellSize, y * cellSize, z*cellSize), Quaternion.identity, gameObject.transform);
                    newCell.CreateCell(false, fullMask, tileObjects.Length, x + (z * dimensionsX) + (y * dimensionsX * dimensionsZ));
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
        // Dynamically instantiate rotated versions of tiles that have rotation flags enabled.
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
                // HORIZONTAL FACES: Verify matching socket names, exclusion rules, and symmetry/flipping states
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

                // VERTICAL FACES: Must be rotationally invariant OR share the same rotation index
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
        // Only destroy the map cells, keep the tileObjects (palette) intact for retry
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
        
        // Destroy rotated tiles created dynamically during generation if they exist
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
