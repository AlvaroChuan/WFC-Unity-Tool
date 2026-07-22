using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.Diagnostics;
using WFC3DMapGenerator;
using Cell3DStructBitmask = WFCStructs.Cell3DStructBitmask;
using Tile3DStructBitmask = WFCStructs.Tile3DStructBitmask;
using Uint2 = WFCStructs.Uint2;

public class WaveFunction3DGPUBitmask : MonoBehaviour
{
    #region Configuration & Inspector
    [Header("Grid Dimensions")]
    [SerializeField] private int dimensionsX;
    [SerializeField] private int dimensionsZ;
    [SerializeField] private int dimensionsY;
    private float cellSize;

    [Header("Core Tiles")]
    [SerializeField] private Tile3D solidTile;
    [SerializeField] private Tile3D emptyTile;
    
    [Header("Assets & Prefabs")]
    [SerializeField] private WFC3DMapGenerator.Tileset tileset;
    [SerializeField] private Cell3D cellObj;
    [SerializeField] private ComputeShader shader;
    #endregion

    #region Internal State
    private int kernel;

    // Data structures (c# only objects)
    private Tile3D[] tileObjects;
    private List<Cell3D> gridComponents;

    // Data structures (structs)
    private Tile3DStructBitmask[] tileObjectsStructs;
    private Cell3DStructBitmask[] gridComponentsStructs;

    // Data structures (buffers)
    private ComputeBuffer tileObjectsBuffer;
    private ComputeBuffer outputBuffer;
    private ComputeBuffer stateBuffer;

    // Generation aux variables
    private bool stopGeneration;
    private bool finished = true;
    private Stopwatch stopwatch;
    #endregion

    #region Main Generation Flow
    public void StartGeneration()
    {
        if (tileset == null || tileset.tiles == null)
        {
            UnityEngine.Debug.LogError("Please assign the Tileset in the inspector to test this standalone.");
            return;
        }
        cellSize = tileset.tileSize;
        Initialize(new Vector3Int(dimensionsX, dimensionsY, dimensionsZ), cellSize, tileset.tiles.ToArray());
    }

    public unsafe void Initialize(Vector3Int mapDimensions, float cellSize, Tile3D[] tiles)
    {
        dimensionsX = mapDimensions.x;
        dimensionsY = mapDimensions.y;
        dimensionsZ = mapDimensions.z;
        this.cellSize = cellSize;
        tileObjects = tiles;
        stopGeneration = false;
        finished = false;

        stopwatch = new Stopwatch();
        stopwatch.Start();

        ClearHierarchy();
        Generate();
    }

    public bool IsFinished()
    {
        return finished;
    }

    unsafe void Generate()
    {
        ClearNeighbours(ref tileObjects);
        CreateRemainingCells(ref tileObjects);
        DefineNeighbourTiles(ref tileObjects, ref tileObjects);

        if (tileObjects.Length > 64)
        {
            UnityEngine.Debug.LogError("Bitmask GPU currently only supports up to 64 variations.");
            return;
        }

        gridComponents = new List<Cell3D>();
        InitializeGrid();

        // Create the structs
        tileObjectsStructs = CreateTile3DStructs();
        gridComponentsStructs = CreateCell3DStructs();
        CreateSolidFloor(gridComponentsStructs);
        CreateEmptyCeiling(gridComponentsStructs);

        // Initialize buffers
        tileObjectsBuffer = new ComputeBuffer(tileObjectsStructs.Length, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Tile3DStructBitmask)));
        outputBuffer = new ComputeBuffer(gridComponentsStructs.Length, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Cell3DStructBitmask)));
        stateBuffer = new ComputeBuffer(1, sizeof(int));

        // Set data
        tileObjectsBuffer.SetData(tileObjectsStructs);

        // Data to buffers
        shader.SetBuffer(0, "tileObjects", tileObjectsBuffer);
        shader.SetBuffer(0, "output", outputBuffer);
        shader.SetBuffer(0, "state", stateBuffer);
        shader.SetInt("totalTiles", tileObjects.Length);
        shader.SetInt("gridDimensionsX", dimensionsX);
        shader.SetInt("gridDimensionsY", dimensionsY);
        shader.SetInt("gridDimensionsZ", dimensionsZ);

        DispatchMap(1);

        // Generate each layer of the map starting from the bottom
        void DispatchMap(int layer)
        {
            DispatchLayer();

            void DispatchLayer()
            {
                if (stopGeneration)
                {
                    finished = true;
                    ReleaseMemory();
                    ClearHierarchy();
                    return;
                }

                Vector3[] offsets = { new Vector3(0, layer, 0), new Vector3(2, layer, 0), new Vector3(0, layer, 2), new Vector3(2, layer, 2) };
                outputBuffer.SetData(gridComponentsStructs);
                stateBuffer.SetData(new int[] { 0 });

                foreach (Vector3 offset in offsets)
                {
                    shader.SetInt("seed", UnityEngine.Random.Range(0, int.MaxValue));
                    shader.SetVector("offset", offset);
                    shader.Dispatch(shader.FindKernel("CSMain"), Mathf.CeilToInt((float)dimensionsX / 10), 1, Mathf.CeilToInt((float)dimensionsZ / 10));
                }

                int[] incompatibilities = new int[1];
                stateBuffer.GetData(incompatibilities);

                if (incompatibilities[0] == 0 & layer > 0 & layer < dimensionsY - 1)
                {
                    layer++;
                    stateBuffer.SetData(new int[1] { 0 });
                    outputBuffer.GetData(gridComponentsStructs);
                    AsyncGPUReadback.Request(outputBuffer, _ => DispatchLayer());
                }
                else if (incompatibilities[0] != 0)
                {
                    stateBuffer.SetData(new int[1] { 0 });
                    outputBuffer.SetData(gridComponentsStructs);
                    AsyncGPUReadback.Request(outputBuffer, _ => DispatchLayer());
                }
                else
                {
                    outputBuffer.GetData(gridComponentsStructs);
                    InstantiateChunk();
                    ClearGeneration();
                    ReleaseMemory();
                    stopwatch.Stop();
                    print($"Map generated completely in {stopwatch.ElapsedMilliseconds} ms ({stopwatch.ElapsedMilliseconds / 1000f} s)");
                }
            }
        }
    }
    #endregion

    #region WFC Constraint Logic (GPU Structs)
    private Uint2 GetMaskFromList(List<Tile3D> list)
    {
        uint lower = 0;
        uint upper = 0;
        foreach (Tile3D t in list)
        {
            int index = Array.IndexOf(tileObjects, t);
            if (index == -1) continue;
            if (index < 32) lower |= (1u << index);
            else upper |= (1u << (index - 32));
        }
        return new Uint2(lower, upper);
    }

    unsafe private Tile3DStructBitmask[] CreateTile3DStructs()
    {
        Tile3DStructBitmask[] tileStructs = new Tile3DStructBitmask[tileObjects.Length];

        for (int i = 0; i < tileObjects.Length; i++)
        {
            Tile3DStructBitmask tileStruct = new Tile3DStructBitmask();
            tileStruct.probability = tileObjects[i].probability;
            tileStruct.rotation = tileObjects[i].rotation;

            tileStruct.validUp = GetMaskFromList(tileObjects[i].upNeighbours);
            tileStruct.validDown = GetMaskFromList(tileObjects[i].downNeighbours);
            tileStruct.validRight = GetMaskFromList(tileObjects[i].rightNeighbours);
            tileStruct.validLeft = GetMaskFromList(tileObjects[i].leftNeighbours);
            tileStruct.validAbove = GetMaskFromList(tileObjects[i].aboveNeighbours);
            tileStruct.validBelow = GetMaskFromList(tileObjects[i].belowNeighbours);
            
            tileStructs[i] = tileStruct;
        }
        return tileStructs;
    }

    unsafe Cell3DStructBitmask[] CreateCell3DStructs()
    {
        Cell3DStructBitmask[] cell3DStructs = new Cell3DStructBitmask[gridComponents.Count];
        
        uint lower = 0;
        uint upper = 0;
        for (int t = 0; t < tileObjects.Length; t++)
        {
            if (t < 32) lower |= (1u << t);
            else upper |= (1u << (t - 32));
        }
        Uint2 fullMask = new Uint2(lower, upper);

        for (int i = 0; i < gridComponents.Count; i++)
        {
            Cell3DStructBitmask cellStruct = new Cell3DStructBitmask();
            cellStruct.collapsed = gridComponents[i].collapsed ? 1u : 0u;
            cellStruct.possibleMask = fullMask;
            cellStruct.entropy = (uint)tileObjects.Length;
            cellStruct.selectedTile = -1;
            cell3DStructs[i] = cellStruct;
        }
        return cell3DStructs;
    }

    unsafe void CreateSolidFloor(Cell3DStructBitmask[] cell3DStructs)
    {
        int solidIndex = Array.IndexOf(tileObjects, solidTile);
        if (solidIndex == -1) return;
        
        Uint2 solidMask = new Uint2(
            (solidIndex < 32) ? (1u << solidIndex) : 0,
            (solidIndex >= 32) ? (1u << (solidIndex - 32)) : 0
        );

        int y = 0;
        for (int z = 0; z < dimensionsZ; z++)
        {
            for (int x = 0; x < dimensionsX; x++)
            {
                int index = x + (z * dimensionsX) + (y * dimensionsX * dimensionsZ);
                cell3DStructs[index].collapsed = 1;
                cell3DStructs[index].entropy = 1;
                cell3DStructs[index].possibleMask = solidMask;
                cell3DStructs[index].selectedTile = solidIndex;
            }
        }
    }

    unsafe void CreateEmptyCeiling(Cell3DStructBitmask[] cell3DStructs)
    {
        int emptyIndex = Array.IndexOf(tileObjects, emptyTile);
        if (emptyIndex == -1) return;

        Uint2 emptyMask = new Uint2(
            (emptyIndex < 32) ? (1u << emptyIndex) : 0,
            (emptyIndex >= 32) ? (1u << (emptyIndex - 32)) : 0
        );

        int y = dimensionsY - 1;
        for (int z = 0; z < dimensionsZ; z++)
        {
            for (int x = 0; x < dimensionsX; x++)
            {
                int index = x + (z * dimensionsX) + (y * dimensionsX * dimensionsZ);
                cell3DStructs[index].collapsed = 1;
                cell3DStructs[index].entropy = 1;
                cell3DStructs[index].possibleMask = emptyMask;
                cell3DStructs[index].selectedTile = emptyIndex;
            }
        }
    }
    #endregion

    #region Grid Initialization
    void InitializeGrid()
    {
        for (int y = 0; y < dimensionsY; y++)
        {
            for (int z = 0; z < dimensionsZ; z++)
            {
                for (int x = 0; x < dimensionsX; x++)
                {
                    Cell3D newCell = Instantiate(cellObj, new Vector3(x * cellSize, y * cellSize, z * cellSize), Quaternion.identity, gameObject.transform);
                    newCell.CreateCell(false, tileObjects, x + (z * dimensionsX) + (y * dimensionsX * dimensionsZ));
                    gridComponents.Add(newCell);
                }
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

    private Tile3D CreateNewTileVariation(Tile3D tile, string nameVariation)
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
            Tile3D[] aux = tileArray.Concat(newTiles.ToArray()).ToArray();
            tileArray = aux;
        }
    }

    private void RotateBorders90(Tile3D originalTile, Tile3D tileRotated)
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

    private void RotateBorders180(Tile3D originalTile, Tile3D tileRotated)
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

    private void RotateBorders270(Tile3D originalTile, Tile3D tileRotated)
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
                    if (tile.upSocket.isSymmetric || otherTile.downSocket.isSymmetric || (otherTile.downSocket.isFlipped && !tile.upSocket.isFlipped) || (!otherTile.downSocket.isFlipped && tile.upSocket.isFlipped))
                        tile.upNeighbours.Add(otherTile);
                }
                
                if (otherTile.upSocket.socket_name == tile.downSocket.socket_name && !tile.excludedNeighboursDown.Contains(otherTile.tileType) && !otherTile.excludedNeighboursUp.Contains(tile.tileType))
                {
                    if (otherTile.upSocket.isSymmetric || tile.downSocket.isSymmetric || (otherTile.upSocket.isFlipped && !tile.downSocket.isFlipped) || (!otherTile.upSocket.isFlipped && tile.downSocket.isFlipped))
                        tile.downNeighbours.Add(otherTile);
                }
                
                if (otherTile.leftSocket.socket_name == tile.rightSocket.socket_name && !tile.excludedNeighboursRight.Contains(otherTile.tileType) && !otherTile.excludedNeighboursLeft.Contains(tile.tileType))
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
                    if ((otherTile.belowSocket.rotationallyInvariant || tile.aboveSocket.rotationallyInvariant) || (otherTile.belowSocket.rotationIndex == tile.aboveSocket.rotationIndex))
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
    private unsafe void InstantiateChunk()
    {
        for (int i = 0; i < gridComponentsStructs.Length; i++)
        {
            int selectedTile = gridComponentsStructs[i].selectedTile;
            if (selectedTile == Array.IndexOf(tileObjects, solidTile) || selectedTile == Array.IndexOf(tileObjects, emptyTile) || selectedTile == -1)
            {
                if (gridComponents[i] != null) DestroyImmediate(gridComponents[i].gameObject);
                gridComponents[i] = null;
            }
            else
            {
                Cell3D cell = gridComponents[i];
                cell.name = "Cell " + i;
                cell.collapsed = gridComponentsStructs[i].collapsed == 1;
                cell.RecreateCell(new Tile3D[] { tileObjects[selectedTile] });
                if (cell.transform.childCount != 0)
                {
                    for(int j = cell.transform.childCount - 1; j >= 0; j--)
                    {
                        Transform child = cell.transform.GetChild(j);
                        DestroyImmediate(child.gameObject);
                    }
                }
                Tile3D instantiatedTile = Instantiate(tileObjects[selectedTile], cell.transform.position, Quaternion.identity, cell.transform);
                if (instantiatedTile.rotation != Vector3.zero)
                {
                    instantiatedTile.gameObject.transform.Rotate(tileObjects[selectedTile].rotation, Space.Self);
                }
                instantiatedTile.gameObject.transform.position += instantiatedTile.positionOffset;
                instantiatedTile.gameObject.SetActive(true);
            }
        }
    }

    public void ClearGeneration()
    {
        List<GameObject> trash = new List<GameObject>();
        for (int i = 0; i < gameObject.transform.childCount; i++)
        {
            Transform child = gameObject.transform.GetChild(i);
            if (child.childCount != 0)
            {
                if (child.gameObject.name.Contains("Cell"))
                {
                    child.GetChild(0).parent = gameObject.transform;
                    trash.Add(child.gameObject);
                }
            }
        }
        foreach (GameObject obj in trash) DestroyImmediate(obj);
    }

    public void ClearHierarchy()
    {
        for (int i = gameObject.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = gameObject.transform.GetChild(i);
            DestroyImmediate(child.gameObject);
        }
    }

    private void ReleaseMemory()
    {
        if (tileObjectsBuffer != null) tileObjectsBuffer.Release();
        if (outputBuffer != null) outputBuffer.Release();
        if (stateBuffer != null) stateBuffer.Release();
        finished = true;
    }

    public void StopGeneration()
    {
        stopGeneration = true;
    }
    #endregion
}
