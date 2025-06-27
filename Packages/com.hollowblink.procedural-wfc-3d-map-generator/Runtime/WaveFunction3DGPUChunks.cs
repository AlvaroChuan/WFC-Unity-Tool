using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;
using System;
using UnityEngine.Rendering;
using Tile3DStruct = WFC3DMapGenerator.WFCStructs.Tile3DStruct;
using Cell3DStruct = WFC3DMapGenerator.WFCStructs.Cell3DStruct;

namespace WFC3DMapGenerator
{
    [ExecuteInEditMode]
    public class WaveFunction3DGPUChunks : MonoBehaviour
    {
        // Constants (must not be changed)
        private const int CHUNK_SIZE = 4;
        private const int MAX_NEIGHBOURS = 50;
        private const int WISH_SUBGRID_SIZE = 12;

        // Map generation parameters
        private float cellSize;
        private int dimensionsX, dimensionsZ, dimensionsY;

        // Shader used for the generation
        [SerializeField] private ComputeShader shader;
        private int kernel;

        // Essential tiles needed for any map
        [SerializeField] private Tile3D solidTile;
        [SerializeField] private Tile3D emptyTile;
        [SerializeField] private GameObject emptyPrefab;
        [SerializeField] private Cell3D cellObj;

        // Data structures (c# only objects)
        private Tile3D[] tileObjects;
        private List<Cell3D> gridComponents;

        // Data structures (structs)
        private Tile3DStruct[] tileObjectsStructs;
        private Cell3DStruct[] gridComponentsStructs;
        private Tuple<Cell3DStruct[], int[]> subGrid;

        // Data structures (buffers)
        private ComputeBuffer tileObjectsBuffer;
        private ComputeBuffer outputBuffer;
        private ComputeBuffer stateBuffer;

        // Generation aux variables
        private List<Vector3Int> chunkOffsets;
        private Vector3Int clampedSubGridSize;
        private int actualChunk;
        private bool stopGeneration;
        private bool finished = true;

        /// <summary>
        /// Initializes the map generation based on the given parameters
        /// </summary>
        /// <param name="mapDimensions"></param> Dimensions of the map to be generated
        /// <param name="cellSize"></param> Size of each cell in the map (tiles ares boxes so the size is the same for all faces)
        /// <param name="tiles"></param> Array of tiles to be used in the map
        public unsafe void Initialize(Vector3Int mapDimensions, float cellSize, Tile3D[] tiles)
        {
            dimensionsX = mapDimensions.x;
            dimensionsY = mapDimensions.y;
            dimensionsZ = mapDimensions.z;
            this.cellSize = cellSize;
            tileObjects = tiles;
            actualChunk = 0;
            stopGeneration = false;
            finished = false;
            ClearHierarchy();
            StartGeneration();
        }

        /// <summary>
        /// Updates the progress of the map generation
        /// </summary>
        /// <returns></returns> Progress of the map generation
        public int GetProgress()
        {
            if (chunkOffsets == null || chunkOffsets.Count == 0) return 0;
            return (int) ((float) actualChunk / (float) (chunkOffsets.Count - 1) * 100f);
        }

        /// <summary>
        /// Checks if the map generation is finished
        /// </summary>
        /// <returns></returns> True if the map generation is finished, false otherwise
        public bool IsFinished()
        {
            return finished;
        }

        /// <summary>
        /// Stops the generation of the map
        /// </summary>
        public void StopGeneration()
        {
            stopGeneration = true;
        }

        /// <summary>
        /// Starts the generation of the map
        /// It creates the tile variations, the grid and the structs needed for the shader
        /// </summary>
        private void StartGeneration()
        {
            ClearNeighbours(ref tileObjects);
            CreateRemainingCells(ref tileObjects);
            DefineNeighbourTiles(ref tileObjects, ref tileObjects);

            gridComponents = new List<Cell3D>();
            InitializeGrid();

            // Create the structs
            tileObjectsStructs = CreateTile3DStructs();
            gridComponentsStructs = CreateCell3DStructs();
            CreateSolidFloor(gridComponentsStructs);
            CreateEmptyCeiling(gridComponentsStructs);

            Vector3Int iterations = new Vector3Int(0, 0, 0);
            iterations.x = Mathf.CeilToInt((float)dimensionsX / (CHUNK_SIZE + 1));
            iterations.y = Mathf.CeilToInt((float)(dimensionsY - 2) / 2);
            iterations.z = Mathf.CeilToInt((float)dimensionsZ / (CHUNK_SIZE + 1));
            chunkOffsets = new List<Vector3Int>();

            for (int y = 0; y < iterations.y; y++)
            {
                for (int z = -1; z < iterations.z; z++)
                {
                    for (int x = -1; x < iterations.x; x++)
                    {
                        chunkOffsets.Add(new Vector3Int(x * CHUNK_SIZE, y * 2, z * CHUNK_SIZE));
                    }
                }
            }

            PrepareChunkDispatch(chunkOffsets[actualChunk]);
        }

        /// <summary>
        /// Prepares the dispatch of a chunk, copying the area need to process that chunk
        /// </summary>
        /// <param name="subGridCoords"></param> Coordinates of the area to be processed in the original grid
        private void PrepareChunkDispatch(Vector3Int subGridCoords, bool clear = false)
        {
            Debug.Log("Dispatching chunk: " + subGridCoords);
            clampedSubGridSize = new Vector3Int(WISH_SUBGRID_SIZE, CHUNK_SIZE, WISH_SUBGRID_SIZE);
            subGrid = GridUtils.ExtractSubGrid(subGridCoords, ref clampedSubGridSize, gridComponentsStructs, new Vector3Int(dimensionsX, dimensionsY, dimensionsZ));

            tileObjectsBuffer = new ComputeBuffer(tileObjectsStructs.Length, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Tile3DStruct)), ComputeBufferType.Structured);
            outputBuffer = new ComputeBuffer(subGrid.Item1.Length, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Cell3DStruct)), ComputeBufferType.Structured);
            stateBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Counter);
            if (!clear) kernel = shader.FindKernel("CSMain");
            else kernel = shader.FindKernel("ClearChunk");

            tileObjectsBuffer.SetData(tileObjectsStructs);
            outputBuffer.SetData(subGrid.Item1);

            Vector3 chunkSubGridCoords = new Vector3(1, subGridCoords.y, 1);
            if (subGridCoords.x == -CHUNK_SIZE) chunkSubGridCoords.x = 0;
            if (subGridCoords.z == -CHUNK_SIZE) chunkSubGridCoords.z = 0;
            DispatchChunk(chunkSubGridCoords);
        }

        /// <summary>
        /// Dispatches the chunk to the GPU
        /// The chunk is a 3x3 subgrid of the original grid, with the middle chunk being the one that is processed.
        /// </summary>
        /// <param name="chunkOffset"></param> Offset of the chunk in the original grid
        private void DispatchChunk(Vector3 chunkOffset)
        {
            // Data to buffers
            shader.SetBuffer(kernel, "tileObjects", tileObjectsBuffer);
            shader.SetBuffer(kernel, "output", outputBuffer);
            shader.SetBuffer(kernel, "state", stateBuffer);
            shader.SetInt("gridDimensionsX", clampedSubGridSize.x);
            shader.SetInt("gridDimensionsY", 3);
            shader.SetInt("gridDimensionsZ", clampedSubGridSize.z);
            shader.SetVector("chunkOffset", chunkOffset); // To make sure that we generate the middle chunk of the 3x3 subGrid
            shader.SetInt("chunkSize", CHUNK_SIZE);

            int layer = 1;
            layer = 1;
            DispatchLayer();

            void DispatchLayer(int attempts = 0, bool clear = false)
            {
                if (stopGeneration)
                {
                    ClearHierarchy();
                    ReleaseMemory();
                    return;
                }

                shader.SetInt("seed", UnityEngine.Random.Range(0, int.MaxValue));
                shader.SetVector("dispatchOffset", new Vector3(0, layer, 0));
                shader.Dispatch(shader.FindKernel("CSMain"), 1, 1, 1);

                int[] state = new int[1];
                stateBuffer.GetData(state);
                if (state[0] == 0 && layer > 0 && layer < 3) //No errors, still layers to process
                {
                    layer++;
                    stateBuffer.SetData(new int[1] { 0 });
                    outputBuffer.GetData(subGrid.Item1);
                    AsyncGPUReadback.Request(outputBuffer, _ => DispatchLayer());
                }
                else if (state[0] != 0)
                {
                    stateBuffer.SetData(new int[1] { 0 });
                    if (attempts < 100)
                    {
                        outputBuffer.SetData(subGrid.Item1);
                        AsyncGPUReadback.Request(outputBuffer, _ => DispatchLayer(++attempts));
                    }
                    else if (actualChunk > 0)
                    {
                        AsyncGPUReadback.Request(stateBuffer, _ => PrepareChunkDispatch(chunkOffsets[--actualChunk], true));
                    }
                    else
                    {
                        ClearHierarchy();
                        ReleaseMemory();
                    }
                }
                else
                {
                    outputBuffer.GetData(subGrid.Item1);
                    GridUtils.CombineGridWithSubgrid(gridComponentsStructs, subGrid.Item1, subGrid.Item2);
                    if (actualChunk < chunkOffsets.Count - 1)
                    {
                        InstantiateChunk(chunkOffsets[actualChunk]);
                        PrepareChunkDispatch(chunkOffsets[++actualChunk]);
                    }
                    else
                    {
                        InstantiateChunk(chunkOffsets[actualChunk]);
                        ClearGeneration();
                        ReleaseMemory();
                    }
                }
            }
        }

        /// <summary>
        /// Instantiates the chunk in the scene
        /// </summary>
        /// <param name="subGridCoords"></param> Coordinates of the chunk in the original grid
        private unsafe void InstantiateChunk(Vector3Int subGridCoords)
        {
            subGridCoords += new Vector3Int(CHUNK_SIZE, 0, CHUNK_SIZE);
            clampedSubGridSize = new Vector3Int(CHUNK_SIZE, CHUNK_SIZE, CHUNK_SIZE);
            Tuple<Cell3DStruct[], int[]> output = GridUtils.ExtractSubGrid(subGridCoords, ref clampedSubGridSize, gridComponentsStructs, new Vector3Int(dimensionsX, dimensionsY, dimensionsZ));
            GameObject Chunk = Instantiate(emptyPrefab, new Vector3((subGridCoords.x + 1) * cellSize, (subGridCoords.y + 1) * cellSize, subGridCoords.z * cellSize), Quaternion.identity);
            Chunk.transform.parent = gameObject.transform;
            Chunk.name = subGridCoords.x + "," + subGridCoords.y + "," + subGridCoords.z;
            for (int i = 0; i < output.Item1.Length; i++)
            {
                if (output.Item1[i].colapsed == 1)
                {
                    if (output.Item1[i].tileOptions[0] == Array.IndexOf(tileObjects, solidTile)
                    || output.Item1[i].tileOptions[0] == Array.IndexOf(tileObjects, emptyTile))
                    {
                        if (gridComponents[output.Item2[i]] != null) DestroyImmediate(gridComponents[output.Item2[i]].gameObject);
                        gridComponents[output.Item2[i]] = null;
                    }
                    else
                    {
                        Cell3D cell = gridComponents[output.Item2[i]];
                        cell.transform.parent = Chunk.transform;
                        cell.name = "Cell " + output.Item2[i];
                        cell.collapsed = output.Item1[i].colapsed == 1;
                        cell.RecreateCell(tileObjects[output.Item1[i].tileOptions[0]]);
                        if (cell.transform.childCount != 0)
                        {
                            for(int j = cell.transform.childCount - 1; j >= 0; j--)
                            {
                                Transform child = cell.transform.GetChild(j);
                                DestroyImmediate(child.gameObject);
                            }
                        }
                        Tile3D instantiatedTile = Instantiate(tileObjects[output.Item1[i].tileOptions[0]], cell.transform.position, Quaternion.identity, cell.transform);
                        if (instantiatedTile.rotation != Vector3.zero)
                        {
                            instantiatedTile.gameObject.transform.Rotate(tileObjects[output.Item1[i].tileOptions[0]].rotation, Space.Self);
                        }
                        instantiatedTile.gameObject.transform.position += instantiatedTile.positionOffset;
                        instantiatedTile.gameObject.SetActive(true);
                    }
                }
            }
        }

        /// <summary>
        /// Clears the hirarchy of chunk to avoid having too many objects in the scene and present a user friendly view
        /// </summary>
        private void ClearGeneration()
        {
            string primaryChunkName, secondaryChunkName;
            string[] primaryCoordinates, secondaryChunkCoordinates;
            int primaryChunkX, primaryChunkY, primaryChunkZ, secondaryChunkX, secondaryChunkY, secondaryChunkZ;

            foreach (Transform chunk in gameObject.transform)
            {
                primaryChunkName = chunk.name;
                primaryCoordinates = primaryChunkName.Split(',');
                if (primaryCoordinates.Length != 3) continue;
                primaryChunkX = int.Parse(primaryCoordinates[0]);
                primaryChunkY = int.Parse(primaryCoordinates[1]);
                primaryChunkZ = int.Parse(primaryCoordinates[2]);

                foreach (Transform secondaryChunk in gameObject.transform)
                {
                    secondaryChunkName = secondaryChunk.name;
                    secondaryChunkCoordinates = secondaryChunkName.Split(',');
                    if (secondaryChunkCoordinates.Length != 3) continue;
                    secondaryChunkX = int.Parse(secondaryChunkCoordinates[0]);
                    secondaryChunkY = int.Parse(secondaryChunkCoordinates[1]);
                    secondaryChunkZ = int.Parse(secondaryChunkCoordinates[2]);
                    if (primaryChunkX == secondaryChunkX && primaryChunkY != secondaryChunkY && primaryChunkZ == secondaryChunkZ && secondaryChunk.childCount != 0)
                    {
                        for (int i = secondaryChunk.childCount - 1; i >= 0; i--)
                        {
                            Transform child = secondaryChunk.GetChild(i);
                            child.parent = chunk;
                        }
                    }
                }

                List<GameObject> trash = new List<GameObject>();
                for (int i = chunk.childCount - 1; i >= 0; i--)
                {
                    Transform child = chunk.GetChild(i);
                    if (child.childCount != 0)
                    {
                        if(child.gameObject.name.Contains("Cell"))
                        {
                            child.GetChild(0).parent = chunk;
                            trash.Add(child.gameObject);
                        }
                    }
                }
                foreach (GameObject obj in trash) DestroyImmediate(obj);
            }

            for (int i = gameObject.transform.childCount - 1; i >= 0; i--)
            {
                Transform chunk = gameObject.transform.GetChild(i);
                if (chunk.transform.childCount == 0) DestroyImmediate(chunk.gameObject);
            }

            Vector2Int chunkCoordinates = new Vector2Int(0, 0);
            int chunksInX = Mathf.CeilToInt((float)dimensionsX / CHUNK_SIZE);
            for (int i = 0; i < gameObject.transform.childCount; i++)
            {
                Transform chunk = gameObject.transform.GetChild(i);
                chunk.name = chunkCoordinates.x + "," + chunkCoordinates.y;
                chunkCoordinates.x++;
                if (chunkCoordinates.x >= chunksInX)
                {
                    chunkCoordinates.x = 0;
                    chunkCoordinates.y++;
                }
            }
        }

        /// <summary>
        /// Clears the hierarchy of the game object
        /// </summary>
        private void ClearHierarchy()
        {
            for(int i = gameObject.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = gameObject.transform.GetChild(i);
                DestroyImmediate(child.gameObject);
            }
        }

        /// <summary>
        /// Releases the memory used by the buffers
        /// </summary>
        private void ReleaseMemory()
        {
            tileObjectsBuffer.Release();
            outputBuffer.Release();
            stateBuffer.Release();
            finished = true;
        }

        /// <summary>
        /// Clears all the tiles' neighbours
        /// </summary>
        /// <param name="tiLeArray"></param> Array of tiles that need to be cleared
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

        /// <summary>
        /// Generates a new tile variation based on a given tile
        /// </summary>
        /// <param name="tile"></param> Tile to be used as base
        /// <param name="nameVariation"></param> Suffix added to the new tile variation
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

            foreach(Component component in tile.gameObject.GetComponents<Component>())
            {
                if (component is Tile3D || component is MeshFilter || component is MeshRenderer || component is Transform) continue;
                Component newComponent = newTile.AddComponent(component.GetType());
                UnityEditor.EditorUtility.CopySerialized(component, newComponent);
            }

            for(int i = 0; i < tile.gameObject.transform.childCount; i++)
            {
                Transform child = tile.gameObject.transform.GetChild(i);
                if (child == tile.gameObject) continue;
                GameObject newChild = Instantiate(child.gameObject, newTile.transform);
            }

            return tileRotated;
        }

        /// <summary>
        /// Generates the tile variations needed to get the full set of possible tiles
        /// based of the initial set of tiles
        /// </summary>
        /// <param name="tileArray"></param> Array of all pre-existing tiles
        private void CreateRemainingCells(ref Tile3D[] tileArray)
        {
            List<Tile3D> newTiles = new List<Tile3D>();
            foreach (Tile3D tile in tileArray)
            {
                // Clockwise by default
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

        /// <summary>
        /// Updates the sockets and excluded neighbours of a tile that has been rotated 90 degrees
        /// </summary>
        /// <param name="originalTile"></param> Non-rotated tile
        /// <param name="tileRotated"></param> Rotated tile
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

            //excluded neighbours
            tileRotated.excludedNeighboursRight = originalTile.excludedNeighboursUp;
            tileRotated.excludedNeighboursLeft = originalTile.excludedNeighboursDown;
            tileRotated.excludedNeighboursUp = originalTile.excludedNeighboursLeft;
            tileRotated.excludedNeighboursDown = originalTile.excludedNeighboursRight;
        }

        /// <summary>
        /// Updates the sockets and excluded neighbours of a tile that has been rotated 180 degrees
        /// </summary>
        /// <param name="originalTile"></param> Non-rotated tile
        /// <param name="tileRotated"></param> Rotated tile
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

            //excluded neighbours
            tileRotated.excludedNeighboursLeft = originalTile.excludedNeighboursRight;
            tileRotated.excludedNeighboursRight = originalTile.excludedNeighboursLeft;
            tileRotated.excludedNeighboursUp = originalTile.excludedNeighboursDown;
            tileRotated.excludedNeighboursDown = originalTile.excludedNeighboursUp;
        }

        /// <summary>
        /// Updates the sockets and excluded neighbours of a tile that has been rotated 270 degrees
        /// </summary>
        /// <param name="originalTile"></param> Non-rotated tile
        /// <param name="tileRotated"></param> Rotated tile
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

            //excluded neighbours
            tileRotated.excludedNeighboursRight = originalTile.excludedNeighboursDown;
            tileRotated.excludedNeighboursLeft = originalTile.excludedNeighboursUp;
            tileRotated.excludedNeighboursUp = originalTile.excludedNeighboursRight;
            tileRotated.excludedNeighboursDown = originalTile.excludedNeighboursLeft;
        }

        /// <summary>
        /// Defines the neighbour tiles of each tile in the array
        /// </summary>
        /// <param name="tileArray"></param> Array of tiles
        /// <param name="otherTileArray"></param> Array of tiles to compare with
        public void DefineNeighbourTiles(ref Tile3D[] tileArray, ref Tile3D[] otherTileArray)
        {
            foreach (Tile3D tile in tileArray)
            {
                foreach (Tile3D otherTile in otherTileArray)
                {
                    // HORIZONTAL FACES: Same socket and be symmetric OR one flip and the other not
                    // It also checks f the excluded list of each face does not include the other tile, and vice versa

                    // Up neighbours
                    if (otherTile.downSocket.socket_name == tile.upSocket.socket_name
                        && !tile.excludedNeighboursUp.Contains(otherTile.tileType)
                        && !otherTile.excludedNeighboursDown.Contains(tile.tileType))
                    {
                        if (tile.upSocket.isSymmetric || otherTile.downSocket.isSymmetric
                        || (otherTile.downSocket.isFlipped && !tile.upSocket.isFlipped)
                        || (!otherTile.downSocket.isFlipped && tile.upSocket.isFlipped))
                            tile.upNeighbours.Add(otherTile);
                    }
                    // Down neighbours
                    if (otherTile.upSocket.socket_name == tile.downSocket.socket_name
                        && !tile.excludedNeighboursDown.Contains(otherTile.tileType)
                        && !otherTile.excludedNeighboursUp.Contains(tile.tileType))
                    {
                        if (otherTile.upSocket.isSymmetric || tile.downSocket.isSymmetric
                        || (otherTile.upSocket.isFlipped && !tile.downSocket.isFlipped)
                        || (!otherTile.upSocket.isFlipped && tile.downSocket.isFlipped))
                            tile.downNeighbours.Add(otherTile);
                    }
                    // Right neighbours
                    if (otherTile.leftSocket.socket_name == tile.rightSocket.socket_name
                        && !tile.excludedNeighboursRight.Contains(otherTile.tileType)
                        && !otherTile.excludedNeighboursLeft.Contains(tile.tileType))
                    {
                        if (otherTile.leftSocket.isSymmetric || tile.rightSocket.isSymmetric
                        || (otherTile.leftSocket.isFlipped && !tile.rightSocket.isFlipped)
                        || (!otherTile.leftSocket.isFlipped && tile.rightSocket.isFlipped))
                            tile.rightNeighbours.Add(otherTile);
                    }
                    // Left neighbours
                    if (otherTile.rightSocket.socket_name == tile.leftSocket.socket_name
                        && !tile.excludedNeighboursLeft.Contains(otherTile.tileType)
                        && !otherTile.excludedNeighboursRight.Contains(tile.tileType))
                    {
                        if (otherTile.rightSocket.isSymmetric || tile.leftSocket.isSymmetric
                            || (otherTile.rightSocket.isFlipped && !tile.leftSocket.isFlipped)
                            || (!otherTile.rightSocket.isFlipped && tile.leftSocket.isFlipped))
                            tile.leftNeighbours.Add(otherTile);
                    }

                    // VERTICAL FACES: both faces must have invariable rotation or the same rotation index

                    // Below neighbours
                    if (otherTile.belowSocket.socket_name == tile.aboveSocket.socket_name)
                    {
                        if ((otherTile.belowSocket.rotationallyInvariant
                            && tile.aboveSocket.rotationallyInvariant)
                            || (otherTile.belowSocket.rotationIndex == tile.aboveSocket.rotationIndex))
                            tile.aboveNeighbours.Add(otherTile);
                    }

                    // Above neighbours
                    if (otherTile.aboveSocket.socket_name == tile.belowSocket.socket_name)
                    {
                        if ((otherTile.aboveSocket.rotationallyInvariant
                            && tile.belowSocket.rotationallyInvariant)
                            || (otherTile.aboveSocket.rotationIndex == tile.belowSocket.rotationIndex))
                            tile.belowNeighbours.Add(otherTile);
                    }
                }
            }
        }

        /// <summary>
        /// Creates the grid structure, filled with cells
        /// </summary>
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

        /// <summary>
        /// Creates the Tile3DStructs needed for the shader
        /// </summary>
        /// <returns></returns> Array of Tile3DStructs
        unsafe private Tile3DStruct[] CreateTile3DStructs()
        {
            Tile3DStruct[] tileStructs = new Tile3DStruct[tileObjects.Length];

            for (int i = 0; i < tileObjects.Length; i++)
            {
                Tile3DStruct tileStruct = new Tile3DStruct();
                tileStruct.probability = tileObjects[i].probability;
                tileStruct.rotation = tileObjects[i].rotation;

                // Initialize neighbours
                for (int j = 0; j < MAX_NEIGHBOURS; j++)
                {
                    tileStruct.upNeighbours[j] = -1;
                    tileStruct.rightNeighbours[j] = -1;
                    tileStruct.downNeighbours[j] = -1;
                    tileStruct.leftNeighbours[j] = -1;
                    tileStruct.aboveNeighbors[j] = -1;
                    tileStruct.belowNeighbours[j] = -1;
                }

                // Copy neighbours (transforming them to indexes)
                for (int j = 0; j < tileObjects[i].upNeighbours.Count; j++)
                {
                    tileStruct.upNeighbours[j] = Array.IndexOf(tileObjects, tileObjects[i].upNeighbours[j]);
                }
                //... and so on for the rest of the neighbours
                for (int j = 0; j < tileObjects[i].rightNeighbours.Count; j++)
                {
                    tileStruct.rightNeighbours[j] = Array.IndexOf(tileObjects, tileObjects[i].rightNeighbours[j]);
                }

                for (int j = 0; j < tileObjects[i].downNeighbours.Count; j++)
                {
                    tileStruct.downNeighbours[j] = Array.IndexOf(tileObjects, tileObjects[i].downNeighbours[j]);
                }

                for (int j = 0; j < tileObjects[i].leftNeighbours.Count; j++)
                {
                    tileStruct.leftNeighbours[j] = Array.IndexOf(tileObjects, tileObjects[i].leftNeighbours[j]);
                }

                for (int j = 0; j < tileObjects[i].aboveNeighbours.Count; j++)
                {
                    tileStruct.aboveNeighbors[j] = Array.IndexOf(tileObjects, tileObjects[i].aboveNeighbours[j]);
                }

                for (int j = 0; j < tileObjects[i].belowNeighbours.Count; j++)
                {
                    tileStruct.belowNeighbours[j] = Array.IndexOf(tileObjects, tileObjects[i].belowNeighbours[j]);
                }
                tileStructs[i] = tileStruct;
            }
            return tileStructs;
        }

        /// <summary>
        /// Creates the Cell3DStructs needed for the shader
        /// </summary>
        /// <returns></returns>
        unsafe Cell3DStruct[] CreateCell3DStructs()
        {
            int[] tileObjectIndexes = new int[tileObjects.Length];

            for (int i = 0; i < tileObjects.Length; i++)
            {
                // Initially all the tiles are possible, so the indexes are the same as the array indexes
                tileObjectIndexes[i] = i;
            }

            Cell3DStruct[] cell3DStructs = new Cell3DStruct[gridComponents.Count];

            for (int i = 0; i < gridComponents.Count; i++)
            {
                Cell3DStruct cellStruct = new Cell3DStruct();
                cellStruct.colapsed = gridComponents[i].collapsed ? 1 : 0;
                for (int j = 0; j < tileObjectIndexes.Length; j++)
                {
                    cellStruct.tileOptions[j] = tileObjectIndexes[j];
                }
                cellStruct.entropy = MAX_NEIGHBOURS;
                cell3DStructs[i] = cellStruct;
            }
            return cell3DStructs;
        }

        /// <summary>
        /// Creates a solid floor of tiles to avoid the generation holes in the first layer
        /// </summary>
        /// <param name="cell3DStructs"></param>
        unsafe void CreateSolidFloor(Cell3DStruct[] cell3DStructs)
        {
            int y = 0;
            for (int z = 0; z < dimensionsZ; z++)
            {
                for (int x = 0; x < dimensionsX; x++)
                {
                    int index = x + (z * dimensionsX) + (y * dimensionsX * dimensionsZ);
                    cell3DStructs[index].colapsed = 1;
                    cell3DStructs[index].entropy = 1;
                    for (int i = 1; i < MAX_NEIGHBOURS; i++)
                    {
                        cell3DStructs[index].tileOptions[i] = -1;
                    }
                    cell3DStructs[index].tileOptions[0] = Array.IndexOf(tileObjects, solidTile);
                }
            }
        }

        /// <summary>
        /// Creates the a ceiling of empty tiles to avoid the generation unfinished layers
        /// </summary>
        /// <param name="cell3DStructs"></param>
        unsafe void CreateEmptyCeiling(Cell3DStruct[] cell3DStructs)
        {
            int y = dimensionsY - 1;
            for (int z = 0; z < dimensionsZ; z++)
            {
                for (int x = 0; x < dimensionsX; x++)
                {
                    int index = x + (z * dimensionsX) + (y * dimensionsX * dimensionsZ);
                    cell3DStructs[index].colapsed = 1;
                    cell3DStructs[index].entropy = 1;
                    for (int i = 1; i < MAX_NEIGHBOURS; i++)
                    {
                        cell3DStructs[index].tileOptions[i] = -1;
                    }
                    cell3DStructs[index].tileOptions[0] = Array.IndexOf(tileObjects, emptyTile);
                }
            }
        }
    }
}