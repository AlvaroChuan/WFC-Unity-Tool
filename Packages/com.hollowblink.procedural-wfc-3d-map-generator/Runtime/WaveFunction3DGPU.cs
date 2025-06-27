using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;
using Cell3DStruct = WFC3DMapGenerator.WFCStructs.Cell3DStruct;
using Tile3DStruct = WFC3DMapGenerator.WFCStructs.Tile3DStruct;

namespace WFC3DMapGenerator
{
    [ExecuteInEditMode]
    public class WaveFunction3DGPU : MonoBehaviour
    {
        // Constants (must not be changed)
        public const int MAX_NEIGHBOURS = 50;

        // Map generation parameters
        private float cellSize;
        private int dimensionsX, dimensionsZ, dimensionsY;

        // Shader used for the generation
        [SerializeField] private ComputeShader shader;
        private int kernel;

        // Essential tiles needed for any map
        [SerializeField] Tile3D solidTile;
        [SerializeField] Tile3D emptyTile;
        [SerializeField] private Cell3D cellObj;

        // Data structures (c# only objects)
        private Tile3D[] tileObjects;
        private List<Cell3D> gridComponents;

        // Data structures (structs)
        private Tile3DStruct[] tileObjectsStructs;
        private Cell3DStruct[] gridComponentsStructs;

        // Data structures (buffers)
        private ComputeBuffer tileObjectsBuffer;
        private ComputeBuffer outputBuffer;
        private ComputeBuffer stateBuffer;

        // Generation aux variables
        private bool stopGeneration;
        private bool finished = true;

        /// <summary>
        /// Initializes the map generation
        /// </summary>
        /// <param name="mapDimensions"></param> Dimensions of the map
        /// <param name="cellSize"></param> Size of each cell
        /// <param name="tiles"></param> Array of tiles to be used
        public unsafe void Initialize(Vector3Int mapDimensions, float cellSize, Tile3D[] tiles)
        {
            dimensionsX = mapDimensions.x;
            dimensionsY = mapDimensions.y;
            dimensionsZ = mapDimensions.z;
            this.cellSize = cellSize;
            tileObjects = tiles;
            stopGeneration = false;
            finished = false;
            ClearHierarchy();
            Generate();
        }

        /// <summary>
        /// Checks if the generation is finished
        /// </summary>
        /// <returns></returns> True if the generation is finished, false otherwise
        public bool IsFinished()
        {
            return finished;
        }

        /// <summary>
        /// Generates the map
        /// </summary>
        unsafe void Generate()
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

            // Initialize buffers
            tileObjectsBuffer = new ComputeBuffer(tileObjectsStructs.Length, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Tile3DStruct)));
            outputBuffer = new ComputeBuffer(gridComponentsStructs.Length, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Cell3DStruct)));
            stateBuffer = new ComputeBuffer(1, sizeof(int));

            // Set data
            tileObjectsBuffer.SetData(tileObjectsStructs);

            // Data to buffers
            shader.SetBuffer(0, "tileObjects", tileObjectsBuffer);
            shader.SetBuffer(0, "output", outputBuffer);
            shader.SetBuffer(0, "state", stateBuffer);
            shader.SetInt("MAX_NEIGHBOURS", MAX_NEIGHBOURS);
            shader.SetInt("gridDimensionsX", dimensionsX);
            shader.SetInt("gridDimensionsY", dimensionsY);
            shader.SetInt("gridDimensionsZ", dimensionsZ);

            // Generate each layer of the map starting from the bottom
            for (int i = 1; i < dimensionsY - 1; i++)
            {
                // Loop until the grid is fully collapsed without any incomatibilities
                if (stopGeneration) break;
                int attempts = 0;
                int[] incompatibilities = { 1 };
                Vector3[] offsets = { new Vector3(0, i, 0), new Vector3(2, i, 0), new Vector3(0, i, 2), new Vector3(2, i, 2) };
                while (incompatibilities[0] != 0 && !stopGeneration)
                {
                    outputBuffer.SetData(gridComponentsStructs);
                    stateBuffer.SetData(new int[] { 0 });
                    foreach (Vector3 offset in offsets)
                    {
                        shader.SetInt("seed", UnityEngine.Random.Range(0, int.MaxValue));
                        shader.SetVector("offset", offset);
                        shader.Dispatch(shader.FindKernel("CSMain"), Mathf.CeilToInt((float)dimensionsX / 10), 1, Mathf.CeilToInt((float)dimensionsZ / 10));
                    }
                    stateBuffer.GetData(incompatibilities);
                    attempts++;
                }
                outputBuffer.GetData(gridComponentsStructs);
            }
            InstantiateChunk();
            ClearGeneration();
            ReleaseMemory();
        }

        /// <summary>
        /// Instantiates the chunk of tiles
        /// </summary>
        private unsafe void InstantiateChunk()
        {
            for (int i = 0; i < gridComponentsStructs.Length; i++)
            {
                if (gridComponentsStructs[i].tileOptions[0] == Array.IndexOf(tileObjects, solidTile)
                || gridComponentsStructs[i].tileOptions[0] == Array.IndexOf(tileObjects, emptyTile))
                {
                    if (gridComponents[i] != null) DestroyImmediate(gridComponents[i].gameObject);
                    gridComponents[i] = null;
                }
                else
                {
                    Cell3D cell = gridComponents[i];
                    cell.name = "Cell " + i;
                    cell.collapsed = gridComponentsStructs[i].colapsed == 1;
                    cell.RecreateCell(tileObjects[gridComponentsStructs[i].tileOptions[0]]);
                    if (cell.transform.childCount != 0)
                    {
                        for(int j = cell.transform.childCount; i >= 0; j--)
                        {
                            Transform child = cell.transform.GetChild(j);
                            DestroyImmediate(child.gameObject);
                        }
                    }
                    Tile3D instantiatedTile = Instantiate(cell.tileOptions[0], cell.transform.position, Quaternion.identity, cell.transform);
                    if (instantiatedTile.rotation != Vector3.zero)
                    {
                        instantiatedTile.gameObject.transform.Rotate(cell.tileOptions[0].rotation, Space.Self);
                    }
                    instantiatedTile.gameObject.transform.position += instantiatedTile.positionOffset;
                    instantiatedTile.gameObject.SetActive(true);
                }
            }
        }

        /// <summary>
        /// Clears the generated tiles from the hierarchy
        /// </summary>
        private void ClearGeneration()
        {
            List<GameObject> trash = new List<GameObject>();
            for (int i = 0; i < gameObject.transform.childCount; i++)
            {
                Transform child = gameObject.transform.GetChild(i);
                if (child.childCount != 0)
                {
                    child.GetChild(0).transform.parent = gameObject.transform;
                    trash.Add(child.gameObject);
                }
            }
            foreach (GameObject obj in trash) DestroyImmediate(obj);
        }

        /// <summary>
        /// Clears the hierarchy of the game object
        /// </summary>
        private void ClearHierarchy()
        {
            for (int i = gameObject.transform.childCount - 1; i >= 0; i--)
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
        /// Stops the generation of the map
        /// </summary>
        public void StopGeneration()
        {
            stopGeneration = true;
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
        /// Creates the grid full of cells
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
                // Initially all the tiles are possible,
                // so the indexes are the same as the array indexes
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
        /// Creates the solid floor of the map
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
        /// Creates the ceiling of the map
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