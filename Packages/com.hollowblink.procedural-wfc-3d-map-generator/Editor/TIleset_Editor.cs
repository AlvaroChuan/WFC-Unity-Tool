using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEditor.VersionControl;
using UnityEngine;
using UnityEngine.UIElements;

namespace WFC3DMapGenerator
{
    public class WFC_Tileset_Editor : EditorWindow
    {
        // UI Elements

        // Preview and main
        [SerializeField] private VisualTreeAsset m_UXML;
        [SerializeField] private VisualElement m_PreviewContainer;
        [SerializeField] private IMGUIContainer m_ImguiContainer;

        // Tileset options
        [SerializeField] private DropdownField m_TilesetDropdown;
        [SerializeField] private TextField m_TilesetNameField;
        [SerializeField] private FloatField m_TileSizeField;

        // Tile options
        [SerializeField] private DropdownField m_TileDropdown;
        [SerializeField] private TextField m_TileNameField;
        [SerializeField] private TextField m_TileTypeField;
        [SerializeField] private IntegerField m_TileProbabilityField;
        [SerializeField] private ObjectField m_SelectedGameObjectField;
        [SerializeField] private Toggle m_TileVariation90, m_TileVariation180, m_TileVariation270;

        // Transform override
        [SerializeField] private Vector3Field m_PositionField, m_RotationField, m_ScaleField;

        // Excluded tile types
        [SerializeField]
        private Foldout m_ExcludedNeighboursFrontFoldout, m_ExcludedNeighboursRightFoldout,
                                         m_ExcludedNeighboursLeftFoldout, m_ExcludedNeighboursBackFoldout;
        [SerializeField]
        private List<Toggle> m_ExcludedNeighboursFrontToggles = new List<Toggle>(), m_ExcludedNeighboursRightToggles = new List<Toggle>(),
                                              m_ExcludedNeighboursLeftToggles = new List<Toggle>(), m_ExcludedNeighboursBackToggles = new List<Toggle>();

        // Socket type creation
        [SerializeField] private TextField m_SocketTypeNameField;
        [SerializeField] private DropdownField m_SocketTypeDropdownField;
        [SerializeField] private Button m_CreateSocketTypeButton, m_DeleteSocketTypeButton;

        // Socket options
        [SerializeField] private DropdownField m_SocketTypeDropdownFront;
        [SerializeField] private Toggle m_SymetricFrontToggle, m_FlippedFrontToggle;
        [SerializeField] private DropdownField m_SocketTypeDropdownRight;
        [SerializeField] private Toggle m_SymetricRightToggle, m_FlippedRightToggle;
        [SerializeField] private DropdownField m_SocketTypeDropdownLeft;
        [SerializeField] private Toggle m_SymetricLeftToggle, m_FlippedLeftToggle;
        [SerializeField] private DropdownField m_SocketTypeDropdownBack;
        [SerializeField] private Toggle m_SymetricBackToggle, m_FlippedBackToggle;
        [SerializeField] private DropdownField m_SocketTypeDropdownTop;
        [SerializeField] private Toggle m_RotationallyInvariantToggleTop;
        [SerializeField] private DropdownField m_SocketTypeDropdownBottom;
        [SerializeField] private Toggle m_RotationallyInvariantToggleBottom;

        // Save button
        [SerializeField] private Button m_SaveButton;

        //--------------------------------------------------------------------------------------------------------------------------------------

        // Internal variables

        // Preview
        [SerializeField] private PreviewRenderUtility m_PreviewRenderUtility;
        [SerializeField] private GameObject m_SocketHelper;
        [SerializeField] private GameObject m_SelectedGameObjectInstance;
        [SerializeField] private GameObject m_SocketHelperInstance;
        [SerializeField] private Bounds m_SelectedGameObjectInstanceBounds;
        [SerializeField] private Vector2 m_PreviewDir = new Vector2(0, 0);
        [SerializeField] private float m_PreviewDistance = 6f;

        // Tileset options
        [SerializeField] private List<Tileset> m_Tilesets = new List<Tileset>();
        [SerializeField] private Tileset m_SelectedTileset = null;
        [SerializeField] private string m_SelectedTilesetName = "";
        [SerializeField] private float m_TileSize = 1f;

        // Tile options
        [SerializeField] private List<Tile3D> m_Tiles = new List<Tile3D>();
        [SerializeField] private Tile3D m_SelectedTile = null;
        [SerializeField] private string m_SelectedTileName = "";
        [SerializeField] private string m_SelectedTileType = "";
        [SerializeField] private int m_SelectedTileProbability = 0;
        [SerializeField] private GameObject m_SelectedGameObject = null;
        [SerializeField] private bool m_Rotate90 = false, m_Rotate180 = false, m_Rotate270 = false;

        // Transform override
        [SerializeField] private Vector3 m_Position = Vector3.zero, m_Rotation = Vector3.zero, m_Scale = Vector3.one;

        // Excluded tile types
        [SerializeField]
        private List<string> m_ExcludedNeighboursFront = new List<string>(), m_ExcludedNeighboursRight = new List<string>(),
                                              m_ExcludedNeighboursLeft = new List<string>(), m_ExcludedNeighboursBack = new List<string>();

        // Socket type creation
        [SerializeField] private string m_SocketTypeName = "";
        [SerializeField] private List<string> m_SocketTypes = new List<string>();

        // Socket options
        [SerializeField] private string m_SocketTypeFront = "", m_SocketTypeRight = "", m_SocketTypeLeft = "", m_SocketTypeBack = "", m_SocketTypeTop = "", m_SocketTypeBottom = "";
        [SerializeField] private bool m_SymetricFront = false, m_SymetricRight = false, m_SymetricLeft = false, m_SymetricBack = false;
        [SerializeField] private bool m_FlippedFront = false, m_FlippedRight = false, m_FlippedLeft = false, m_FlippedBack = false;
        [SerializeField] private bool m_RotationallyInvariantTop = false, m_RotationallyInvariantBottom = false;
        [SerializeField] private Tile3D m_EmptyTile = null, m_SolidTile = null;

        // UI state
        [SerializeField] private bool m_UIInitialized = false;

        //--------------------------------------------------------------------------------------------------------------------------------------

        [MenuItem("Tools/WFC Generation/WFC Tileset Editor")]
        public static void ShowWindow()
        {
            var window = GetWindow<WFC_Tileset_Editor>();
            window.titleContent = new GUIContent("WFC Tileset Editor");
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            m_UXML.CloneTree(root);

            // Tileset dropdown
            m_TilesetDropdown = root.Q<DropdownField>("tilesetDropdown");
            if (m_TilesetDropdown != null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources/Tilesets")) AssetDatabase.CreateFolder("Assets/Resources", "Tilesets");
                m_TilesetDropdown.choices = Resources.LoadAll<Tileset>("Tilesets/")
                        .Select(asset => asset.name)
                        .ToList();
                m_TilesetDropdown.choices.Add("New tileset");

                m_SelectedTileset = Resources.Load<Tileset>($"Tilesets/{m_TilesetDropdown.choices[0]}");
                if (m_SelectedTileset != null)
                {
                    m_TilesetDropdown.value = m_SelectedTileset.name;
                    m_SelectedTilesetName = m_SelectedTileset.name;
                }
                else
                {
                    m_TilesetDropdown.value = "New tileset";
                    m_SelectedTilesetName = "New tileset";
                }

                m_TilesetDropdown.RegisterValueChangedCallback(SelectTileset);
            }

            // Tileset name field
            m_TilesetNameField = root.Q<TextField>("tilesetNameField");
            if (m_TilesetNameField != null)
            {
                m_TilesetNameField.value = m_SelectedTilesetName;
                m_TilesetNameField.RegisterValueChangedCallback(ChangeTilesetName);
            }

            // Tile size field
            m_TileSizeField = root.Q<FloatField>("tileSizeField");
            if (m_TileSizeField != null)
            {
                if (m_SelectedTileset != null) m_TileSize = m_SelectedTileset.tileSize;
                m_TileSizeField.value = m_TileSize;
                m_TileSizeField.RegisterValueChangedCallback(ChangeTileSize);
            }

            // Tile dropdown
            m_TileDropdown = root.Q<DropdownField>("tileDropdown");
            if (m_TileDropdown != null)
            {
                if (m_SelectedTileset != null) m_Tiles = m_SelectedTileset.tiles;
                m_TileDropdown.choices = m_Tiles
                    .Where(tile => tile.name != "EMPTY" && tile.name != "SOLID")
                    .Select(tile => tile.name)
                    .ToList();
                m_TileDropdown.choices.Add("New tile");
                m_TileDropdown.value = m_TileDropdown.choices[0];
                if (m_TileDropdown.value != "New tile") m_SelectedTile = m_Tiles.FirstOrDefault(tile => tile.name == m_TileDropdown.value);
                else m_SelectedTile = null;
                ChangeTile(m_TileDropdown.value, false);
                m_TileDropdown.RegisterValueChangedCallback(ChangeTile);
            }

            // Tile name field
            m_TileNameField = root.Q<TextField>("tileNameField");
            if (m_TileNameField != null)
            {
                m_TileNameField.value = m_SelectedTileName;
                m_TileNameField.RegisterValueChangedCallback(ChangeTileName);
            }

            // Tile type field
            m_TileTypeField = root.Q<TextField>("tileTypeField");
            if (m_TileTypeField != null)
            {
                m_TileTypeField.value = m_SelectedTileType;
                m_TileTypeField.RegisterValueChangedCallback(ChangeTileType);
            }

            // Tile probability field
            m_TileProbabilityField = root.Q<IntegerField>("tileProbabilityField");
            if (m_TileProbabilityField != null)
            {
                m_TileProbabilityField.value = m_SelectedTileProbability;
                m_TileProbabilityField.RegisterValueChangedCallback(evt =>
                {
                    m_SelectedTileProbability = evt.newValue;
                    m_SelectedTile.probability = m_SelectedTileProbability;
                });
            }

            // Object field
            m_SelectedGameObjectField = root.Q<ObjectField>("tilePrefabField");
            if (m_SelectedGameObjectField != null)
            {
                m_SelectedGameObjectField.objectType = typeof(GameObject);
                m_SelectedGameObjectField.value = m_SelectedGameObject;
                m_SelectedGameObjectField.RegisterValueChangedCallback(ChangeGameObject);
            }

            // Tile variations
            m_TileVariation90 = root.Q<Toggle>("tileVariationToggle90");
            if (m_TileVariation90 != null)
            {
                m_TileVariation90.value = m_Rotate90;
                m_TileVariation90.RegisterValueChangedCallback(evt => m_Rotate90 = evt.newValue);
            }

            m_TileVariation180 = root.Q<Toggle>("tileVariationToggle180");
            if (m_TileVariation180 != null)
            {
                m_TileVariation180.value = m_Rotate180;
                m_TileVariation180.RegisterValueChangedCallback(evt => m_Rotate180 = evt.newValue);
            }

            m_TileVariation270 = root.Q<Toggle>("tileVariationToggle270");
            if (m_TileVariation270 != null)
            {
                m_TileVariation270.value = m_Rotate270;
                m_TileVariation270.RegisterValueChangedCallback(evt => m_Rotate270 = evt.newValue);
            }

            // Transform override
            m_PositionField = root.Q<Vector3Field>("positionField");
            if (m_PositionField != null)
            {
                m_PositionField.value = m_Position;
                m_PositionField.RegisterValueChangedCallback(evt => m_Position = evt.newValue);
            }

            m_RotationField = root.Q<Vector3Field>("rotationField");
            if (m_RotationField != null)
            {
                m_RotationField.value = m_Rotation;
                m_RotationField.RegisterValueChangedCallback(evt => m_Rotation = evt.newValue);
            }

            m_ScaleField = root.Q<Vector3Field>("scaleField");
            if (m_ScaleField != null)
            {
                m_ScaleField.value = m_Scale;
                m_ScaleField.RegisterValueChangedCallback(evt => m_Scale = evt.newValue);
            }

            // Excluded tile types
            m_ExcludedNeighboursFrontFoldout = root.Q<Foldout>("excludedNeighboursFront");
            if (m_ExcludedNeighboursFrontFoldout != null)
            {
                if (m_SelectedTileset != null)
                {
                    m_ExcludedNeighboursFrontToggles = new List<Toggle>();
                    foreach (string tileType in m_SelectedTileset.tileTypes)
                    {
                        Toggle toggle = new Toggle(tileType);
                        toggle.value = m_ExcludedNeighboursFront.Contains(tileType);
                        toggle.RegisterValueChangedCallback(evt =>
                        {
                            if (evt.newValue) m_ExcludedNeighboursFront.Add(tileType);
                            else m_ExcludedNeighboursFront.Remove(tileType);
                        });
                        m_ExcludedNeighboursFrontFoldout.Add(toggle);
                        m_ExcludedNeighboursFrontToggles.Add(toggle);
                    }
                }
            }

            m_ExcludedNeighboursRightFoldout = root.Q<Foldout>("excludedNeighboursRight");
            if (m_ExcludedNeighboursRightFoldout != null)
            {
                if (m_SelectedTileset != null)
                {
                    m_ExcludedNeighboursRightToggles = new List<Toggle>();
                    foreach (string tileType in m_SelectedTileset.tileTypes)
                    {
                        Toggle toggle = new Toggle(tileType);
                        toggle.value = m_ExcludedNeighboursRight.Contains(tileType);
                        toggle.RegisterValueChangedCallback(evt =>
                        {
                            if (evt.newValue) m_ExcludedNeighboursRight.Add(tileType);
                            else m_ExcludedNeighboursRight.Remove(tileType);
                        });
                        m_ExcludedNeighboursRightFoldout.Add(toggle);
                        m_ExcludedNeighboursRightToggles.Add(toggle);
                    }
                }
            }

            m_ExcludedNeighboursLeftFoldout = root.Q<Foldout>("excludedNeighboursLeft");
            if (m_ExcludedNeighboursLeftFoldout != null)
            {
                if (m_SelectedTileset != null)
                {
                    m_ExcludedNeighboursLeftToggles = new List<Toggle>();
                    foreach (string tileType in m_SelectedTileset.tileTypes)
                    {
                        Toggle toggle = new Toggle(tileType);
                        toggle.value = m_ExcludedNeighboursLeft.Contains(tileType);
                        toggle.RegisterValueChangedCallback(evt =>
                        {
                            if (evt.newValue) m_ExcludedNeighboursLeft.Add(tileType);
                            else m_ExcludedNeighboursLeft.Remove(tileType);
                        });
                        m_ExcludedNeighboursLeftFoldout.Add(toggle);
                        m_ExcludedNeighboursLeftToggles.Add(toggle);
                    }
                }
            }

            m_ExcludedNeighboursBackFoldout = root.Q<Foldout>("excludedNeighboursBack");
            if (m_ExcludedNeighboursBackFoldout != null)
            {
                if (m_SelectedTileset != null)
                {
                    m_ExcludedNeighboursBackToggles = new List<Toggle>();
                    foreach (string tileType in m_SelectedTileset.tileTypes)
                    {
                        Toggle toggle = new Toggle(tileType);
                        toggle.value = m_ExcludedNeighboursBack.Contains(tileType);
                        toggle.RegisterValueChangedCallback(evt =>
                        {
                            if (evt.newValue) m_ExcludedNeighboursBack.Add(tileType);
                            else m_ExcludedNeighboursBack.Remove(tileType);
                        });
                        m_ExcludedNeighboursBackFoldout.Add(toggle);
                        m_ExcludedNeighboursBackToggles.Add(toggle);
                    }
                }
            }

            // Type creation
            m_SocketTypeNameField = root.Q<TextField>("socketTypeNameField");
            if (m_SocketTypeNameField != null)
            {
                m_SocketTypeNameField.value = m_SocketTypeName;
                m_SocketTypeNameField.RegisterValueChangedCallback(evt => m_SocketTypeName = evt.newValue);
            }

            m_SocketTypeDropdownField = root.Q<DropdownField>("selectedSocketTypeField");
            if (m_SocketTypeDropdownField != null)
            {
                if (m_SelectedTileset != null) m_SocketTypes = m_SelectedTileset.socketTypes;
                m_SocketTypeDropdownField.choices = m_SocketTypes.Where(socketType => socketType != "Empty" && socketType != "Solid").ToList(); ;
                m_SocketTypeDropdownField.value = m_SocketTypeName;
                m_SocketTypeDropdownField.RegisterValueChangedCallback(evt => m_SocketTypeName = evt.newValue);
            }

            m_CreateSocketTypeButton = root.Q<Button>("createSocketTypeButton");
            if (m_CreateSocketTypeButton != null)
            {
                m_CreateSocketTypeButton.RegisterCallback<ClickEvent>(evt =>
                {
                    if (m_SocketTypes == null) m_SocketTypes = new List<string>();
                    if (!m_SocketTypes.Contains(m_SocketTypeName)) m_SocketTypes.Add(m_SocketTypeName);
                    m_SocketTypeDropdownField.choices = m_SocketTypes.Where(socketType => socketType != "Empty" && socketType != "Solid").ToList(); ;
                    m_SocketTypeDropdownField.value = m_SocketTypeName;
                });
            }

            m_DeleteSocketTypeButton = root.Q<Button>("deleteSocketTypeButton");
            if (m_DeleteSocketTypeButton != null)
            {
                m_DeleteSocketTypeButton.RegisterCallback<ClickEvent>(evt =>
                {
                    if (m_SocketTypes != null && m_SocketTypes.Contains(m_SocketTypeName))
                    {
                        m_SocketTypes.Remove(m_SocketTypeName);
                        m_SocketTypeDropdownField.choices = m_SocketTypes.Where(socketType => socketType != "Empty" && socketType != "Solid").ToList(); ;
                        if (m_SocketTypes.Count > 0 && m_SocketTypes[0] != "Empty" && m_SocketTypes[0] != "Solid")
                        {
                            m_SocketTypeDropdownField.value = m_SocketTypes[0];
                            m_SocketTypeNameField.value = m_SocketTypes[0];
                        }
                        else
                        {
                            m_SocketTypeDropdownField.value = "";
                            m_SocketTypeNameField.value = "";
                        }
                        foreach (Tile3D tile in m_SelectedTileset.tiles)
                        {
                            if (tile.upSocket.socket_name == m_SocketTypeName) tile.upSocket.socket_name = "";
                            if (tile.rightSocket.socket_name == m_SocketTypeName) tile.rightSocket.socket_name = "";
                            if (tile.downSocket.socket_name == m_SocketTypeName) tile.downSocket.socket_name = "";
                            if (tile.leftSocket.socket_name == m_SocketTypeName) tile.leftSocket.socket_name = "";
                            if (tile.aboveSocket.socket_name == m_SocketTypeName) tile.aboveSocket.socket_name = "";
                            if (tile.belowSocket.socket_name == m_SocketTypeName) tile.belowSocket.socket_name = "";
                        }
                        m_SocketTypeName = "";
                    }
                });
            }

            // Socket options front
            m_SocketTypeDropdownFront = root.Q<DropdownField>("socketTypeDropdownFront");
            if (m_SocketTypeDropdownFront != null)
            {
                m_SocketTypeDropdownFront.choices = m_SocketTypes;
                m_SocketTypeDropdownFront.value = m_SocketTypeFront;
                m_SocketTypeDropdownFront.RegisterValueChangedCallback(evt => m_SocketTypeFront = evt.newValue);
            }

            m_SymetricFrontToggle = root.Q<Toggle>("symetricFrontField");
            if (m_SymetricFrontToggle != null)
            {
                m_SymetricFrontToggle.value = m_SymetricFront;
                m_SymetricFrontToggle.RegisterValueChangedCallback(evt => m_SymetricFront = evt.newValue);
            }

            m_FlippedFrontToggle = root.Q<Toggle>("flippedFrontField");
            if (m_FlippedFrontToggle != null)
            {
                m_FlippedFrontToggle.value = m_FlippedFront;
                m_FlippedFrontToggle.RegisterValueChangedCallback(evt => m_FlippedFront = evt.newValue);
            }

            // Socket options right
            m_SocketTypeDropdownRight = root.Q<DropdownField>("socketTypeDropdownRight");
            if (m_SocketTypeDropdownRight != null)
            {
                m_SocketTypeDropdownRight.choices = m_SocketTypes;
                m_SocketTypeDropdownRight.value = m_SocketTypeRight;
                m_SocketTypeDropdownRight.RegisterValueChangedCallback(evt => m_SocketTypeRight = evt.newValue);
            }

            m_SymetricRightToggle = root.Q<Toggle>("symetricRightField");
            if (m_SymetricRightToggle != null)
            {
                m_SymetricRightToggle.value = m_SymetricRight;
                m_SymetricRightToggle.RegisterValueChangedCallback(evt => m_SymetricRight = evt.newValue);
            }

            m_FlippedRightToggle = root.Q<Toggle>("flippedRightField");
            if (m_FlippedRightToggle != null)
            {
                m_FlippedRightToggle.value = m_FlippedRight;
                m_FlippedRightToggle.RegisterValueChangedCallback(evt => m_FlippedRight = evt.newValue);
            }

            // Socket options left
            m_SocketTypeDropdownLeft = root.Q<DropdownField>("socketTypeDropdownLeft");
            if (m_SocketTypeDropdownLeft != null)
            {
                m_SocketTypeDropdownLeft.choices = m_SocketTypes;
                m_SocketTypeDropdownLeft.value = m_SocketTypeLeft;
                m_SocketTypeDropdownLeft.RegisterValueChangedCallback(evt => m_SocketTypeLeft = evt.newValue);
            }

            m_SymetricLeftToggle = root.Q<Toggle>("symetricLeftField");
            if (m_SymetricLeftToggle != null)
            {
                m_SymetricLeftToggle.value = m_SymetricLeft;
                m_SymetricLeftToggle.RegisterValueChangedCallback(evt => m_SymetricLeft = evt.newValue);
            }

            m_FlippedLeftToggle = root.Q<Toggle>("flippedLeftField");
            if (m_FlippedLeftToggle != null)
            {
                m_FlippedLeftToggle.value = m_FlippedLeft;
                m_FlippedLeftToggle.RegisterValueChangedCallback(evt => m_FlippedLeft = evt.newValue);
            }

            // Socket options back
            m_SocketTypeDropdownBack = root.Q<DropdownField>("socketTypeDropdownBack");
            if (m_SocketTypeDropdownBack != null)
            {
                m_SocketTypeDropdownBack.choices = m_SocketTypes;
                m_SocketTypeDropdownBack.value = m_SocketTypeBack;
                m_SocketTypeDropdownBack.RegisterValueChangedCallback(evt => m_SocketTypeBack = evt.newValue);
            }

            m_SymetricBackToggle = root.Q<Toggle>("symetricBackField");
            if (m_SymetricBackToggle != null)
            {
                m_SymetricBackToggle.value = m_SymetricBack;
                m_SymetricBackToggle.RegisterValueChangedCallback(evt => m_SymetricBack = evt.newValue);
            }

            m_FlippedBackToggle = root.Q<Toggle>("flippedBackField");
            if (m_FlippedBackToggle != null)
            {
                m_FlippedBackToggle.value = m_FlippedBack;
                m_FlippedBackToggle.RegisterValueChangedCallback(evt => m_FlippedBack = evt.newValue);
            }

            // Socket options top
            m_SocketTypeDropdownTop = root.Q<DropdownField>("socketTypeDropdownTop");
            if (m_SocketTypeDropdownTop != null)
            {
                m_SocketTypeDropdownTop.choices = m_SocketTypes;
                m_SocketTypeDropdownTop.value = m_SocketTypeTop;
                m_SocketTypeDropdownTop.RegisterValueChangedCallback(evt => m_SocketTypeTop = evt.newValue);
            }

            m_RotationallyInvariantToggleTop = root.Q<Toggle>("rotationalIyInvariantTopField");
            if (m_RotationallyInvariantToggleTop != null)
            {
                m_RotationallyInvariantToggleTop.value = m_RotationallyInvariantTop;
                m_RotationallyInvariantToggleTop.RegisterValueChangedCallback(evt => m_RotationallyInvariantTop = evt.newValue);
            }

            // Socket options bottom
            m_SocketTypeDropdownBottom = root.Q<DropdownField>("socketTypeDropdownBottom");
            if (m_SocketTypeDropdownBottom != null)
            {
                m_SocketTypeDropdownBottom.choices = m_SocketTypes;
                m_SocketTypeDropdownBottom.value = m_SocketTypeBottom;
                m_SocketTypeDropdownBottom.RegisterValueChangedCallback(evt => m_SocketTypeBottom = evt.newValue);
            }

            m_RotationallyInvariantToggleBottom = root.Q<Toggle>("rotationalIyInvariantBottomField");
            if (m_RotationallyInvariantToggleBottom != null)
            {
                m_RotationallyInvariantToggleBottom.value = m_RotationallyInvariantBottom;
                m_RotationallyInvariantToggleBottom.RegisterValueChangedCallback(evt => m_RotationallyInvariantBottom = evt.newValue);
            }

            // Save button
            m_SaveButton = root.Q<Button>("saveButton");
            if (m_SaveButton != null)
            {
                m_SaveButton.RegisterCallback<ClickEvent>(SaveTile);
            }

            // Preiew utility
            m_PreviewContainer = root.Q<VisualElement>("render3DContainer");
            m_PreviewRenderUtility = new PreviewRenderUtility();
            m_PreviewRenderUtility.cameraFieldOfView = 30f;
            m_PreviewRenderUtility.ambientColor = Color.gray;

            // Instantiate the socket helper and selected game object
            if (m_SelectedGameObject != null) m_SelectedGameObjectInstance = Instantiate(m_SelectedGameObject);
            if (m_SelectedGameObject != null) m_SelectedGameObjectInstance.hideFlags = HideFlags.HideAndDontSave;
            m_SocketHelperInstance = Instantiate(m_SocketHelper);
            m_SocketHelperInstance.hideFlags = HideFlags.HideAndDontSave;

            // Adjust based on the size of the selected game object
            if (m_SelectedGameObject != null) m_SelectedGameObjectInstanceBounds = GetBounds(m_SelectedGameObjectInstance);
            if (m_SelectedGameObject != null) m_SocketHelperInstance.transform.position = m_SelectedGameObjectInstanceBounds.center;
            else m_SocketHelperInstance.transform.position = Vector3.zero;
            m_SocketHelperInstance.transform.localScale = new Vector3(m_TileSize + 0.01f, m_TileSize + 0.01f, m_TileSize + 0.01f); //TODO: let the user decide
            m_PreviewRenderUtility.AddSingleGO(m_SocketHelperInstance);
            if (m_SelectedGameObject != null) m_PreviewRenderUtility.AddSingleGO(m_SelectedGameObjectInstance);

            // Camera settings
            m_PreviewRenderUtility.camera.clearFlags = CameraClearFlags.Color;
            m_PreviewRenderUtility.camera.backgroundColor = new Color(0, 0, 0, 0);
            m_PreviewRenderUtility.camera.nearClipPlane = 0.1f;
            m_PreviewRenderUtility.camera.farClipPlane = 25f;

            m_ImguiContainer = root.Q<IMGUIContainer>("IMGUIContainer");
            m_ImguiContainer.onGUIHandler = DrawPreview;

            m_UIInitialized = true;

        }

        /// <summary>
        /// Called when the user selects the window
        /// </summary>
        private void OnFocus()
        {
            m_Tilesets = Resources.LoadAll<Tileset>("Tilesets/").ToList();
            if (!m_UIInitialized) CreateGUI();
            m_TilesetDropdown.choices = m_Tilesets.Select(tileset => tileset.name).ToList();
            m_TilesetDropdown.choices.Add("New tileset");
            if (m_SelectedTileset != null) m_TilesetDropdown.value = m_SelectedTileset.name;
            else
            {
                m_TilesetDropdown.value = "New tileset";
                SelectTilesetDirect("New tileset");
            }
            if (AssetDatabase.IsValidFolder($"Assets/Resources/Tiles/{m_SelectedTilesetName}"))
            {
                m_SelectedTileset.tiles = Resources.LoadAll<Tile3D>($"Tiles/{m_SelectedTilesetName}/").ToList();
            }
            else m_SelectedTileset.tiles = new List<Tile3D>();
            m_SelectedTileset.tiles.Add(m_EmptyTile);
            m_SelectedTileset.tiles.Add(m_SolidTile);
            m_SelectedTileset.tileCount = m_SelectedTileset.tiles.Count;
            m_Tiles = m_SelectedTileset.tiles;
            m_TileDropdown.choices = m_Tiles
                .Where(tile => tile.name != "EMPTY" && tile.name != "SOLID")
                .Select(tile => tile.name)
                .ToList();
            m_TileDropdown.choices.Add("New tile");
            if (m_SelectedTile != null && m_Tiles.Contains(m_SelectedTile)) m_TileDropdown.value = m_SelectedTile.name;
            else m_TileDropdown.value = m_TileDropdown.choices[^1];
        }

        /// <summary>
        /// Draw the preview of the selected tile.
        /// </summary>
        private void OnDisable()
        {
            if (m_PreviewRenderUtility != null)
            {
                m_PreviewRenderUtility.Cleanup();
                m_PreviewRenderUtility = null;
            }

            if (m_SelectedGameObjectInstance != null) DestroyImmediate(m_SelectedGameObjectInstance);
        }

        /// <summary>
        /// Call select tileset with the given name.
        /// </summary>
        /// <param name="tilesetName"></param> Name of the tileset to select
        private void SelectTilesetDirect(string tilesetName)
        {
            if (tilesetName == "New tileset")
            {
                m_SelectedTileset = CreateInstance<Tileset>();
                m_SelectedTileset.tiles.Add(m_EmptyTile);
                m_SelectedTileset.tiles.Add(m_SolidTile);
                m_SelectedTileset.socketTypes.Add("Empty");
                m_SelectedTileset.socketTypes.Add("Solid");
            }
            else m_SelectedTileset = Resources.Load<Tileset>($"Tilesets/{tilesetName}");
            m_SelectedTilesetName = tilesetName;
            m_TilesetNameField.value = m_SelectedTilesetName;
            m_TileSize = m_SelectedTileset.tileSize;
            m_TileSizeField.value = m_TileSize;
            m_Tiles = m_SelectedTileset.tiles;
            m_TileDropdown.choices = m_Tiles
                .Where(tile => tile.name != "EMPTY" && tile.name != "SOLID")
                .Select(tile => tile.name)
                .ToList();
            m_TileDropdown.choices.Add("New tile");
            m_TileDropdown.value = m_TileDropdown.choices[0];
            m_SocketTypes = m_SelectedTileset.socketTypes;
            m_SocketTypeDropdownField.choices = m_SocketTypes.Where(socketType => socketType != "Empty" && socketType != "Solid").ToList();
            m_SocketTypeDropdownField.value = "";
            m_SocketTypeNameField.value = "";
            ChangeTile(m_TileDropdown.value);
        }

        /// <summary>
        /// Draw the preview of the selected tile.
        /// </summary>
        /// <param name="evt"></param> Change event
        private void SelectTileset(ChangeEvent<string> evt)
        {
            if (evt.newValue == "New tileset" || Resources.Load<Tileset>($"Tilesets/{evt.newValue}") == null)
            {
                m_SelectedTileset = CreateInstance<Tileset>();
                m_SelectedTileset.tiles.Add(m_EmptyTile);
                m_SelectedTileset.tiles.Add(m_SolidTile);
                m_SelectedTileset.socketTypes.Add("Empty");
                m_SelectedTileset.socketTypes.Add("Solid");
            }
            else m_SelectedTileset = Resources.Load<Tileset>($"Tilesets/{evt.newValue}");
            m_SelectedTilesetName = evt.newValue;
            m_TilesetNameField.value = m_SelectedTilesetName;
            m_TileSize = m_SelectedTileset.tileSize;
            m_TileSizeField.value = m_TileSize;
            m_Tiles = m_SelectedTileset.tiles;
            m_TileDropdown.choices = m_Tiles
                .Where(tile => tile.name != "EMPTY" && tile.name != "SOLID")
                .Select(tile => tile.name)
                .ToList();
            m_TileDropdown.choices.Add("New tile");
            m_TileDropdown.value = m_TileDropdown.choices[0];
            m_SocketTypes = m_SelectedTileset.socketTypes;
            m_SocketTypeDropdownField.choices = m_SocketTypes.Where(socketType => socketType != "Empty" && socketType != "Solid").ToList();
            m_SocketTypeDropdownField.value = "";
            m_SocketTypeNameField.value = "";
            ChangeTile(m_TileDropdown.value);
        }

        /// <summary>
        /// Change the selected tile name in the TextField.
        /// </summary>
        /// <param name="evt"></param> Change event
        private void ChangeTilesetName(ChangeEvent<string> evt)
        {
            m_SelectedTilesetName = evt.newValue;
            m_TilesetNameField.value = m_SelectedTilesetName;
        }

        /// <summary>
        /// Change the selected tile name in the TextField.
        /// </summary>
        /// <param name="evt"></param> Change event
        private void ChangeTileSize(ChangeEvent<float> evt)
        {
            m_TileSize = evt.newValue;
            if(m_SelectedTileset != null) m_SelectedTileset.tileSize = m_TileSize;
            if (m_SocketHelperInstance != null) m_SocketHelperInstance.transform.localScale = new Vector3(m_TileSize + 0.01f, m_TileSize + 0.01f, m_TileSize + 0.01f);
        }

        /// <summary>
        /// Change the selected tile name in the TextField.
        /// </summary>
        /// <param name="evt"></param>
        private void ChangeTile(ChangeEvent<string> evt)
        {
            ChangeTile(evt.newValue);
        }

        /// <summary>
        /// Change the selected tile name in the TextField.
        /// This is used to set the tile name for the selected tile.
        /// </summary>
        /// <param name="tileName"></param> Name of the new tile
        /// <param name="updateUI"></param> Whether to update the UI or not
        private void ChangeTile(string tileName, bool updateUI = true)
        {
            if (tileName == "New tile")
            {
                m_SelectedTile = null;
                m_SelectedTileName = tileName;
                m_SelectedTileType = "";
                m_SelectedTileProbability = 1;
                m_SelectedGameObject = null;
                m_Rotate90 = false;
                m_Rotate180 = false;
                m_Rotate270 = false;
                m_Position = Vector3.zero;
                m_Rotation = Vector3.zero;
                m_Scale = Vector3.zero;
                m_ExcludedNeighboursFront = new List<string>();
                m_ExcludedNeighboursRight = new List<string>();
                m_ExcludedNeighboursLeft = new List<string>();
                m_ExcludedNeighboursBack = new List<string>();
                m_SocketTypeFront = "";
                m_SocketTypeRight = "";
                m_SocketTypeLeft = "";
                m_SocketTypeBack = "";
                m_SocketTypeTop = "";
                m_SocketTypeBottom = "";
                m_SymetricFront = false;
                m_SymetricRight = false;
                m_SymetricLeft = false;
                m_SymetricBack = false;
                m_FlippedFront = false;
                m_FlippedRight = false;
                m_FlippedLeft = false;
                m_FlippedBack = false;
                m_RotationallyInvariantTop = false;
                m_RotationallyInvariantBottom = false;
            }
            else
            {
                m_SelectedTile = m_Tiles.FirstOrDefault(tile => tile.name == tileName);
                if (m_SelectedTile != null)
                {
                    m_SelectedTileName = m_SelectedTile.name;
                    m_SelectedTileType = m_SelectedTile.tileType;
                    m_SelectedTileProbability = m_SelectedTile.probability;
                    m_SelectedGameObject = m_SelectedTile.gameObject;
                    m_Rotate90 = m_SelectedTile.rotateRight;
                    m_Rotate180 = m_SelectedTile.rotate180;
                    m_Rotate270 = m_SelectedTile.rotateLeft;
                    m_Position = m_SelectedTile.positionOffset;
                    m_Rotation = m_SelectedTile.rotation;
                    m_Scale = m_SelectedTile.scale;
                    m_ExcludedNeighboursFront = new List<string>(m_SelectedTile.excludedNeighboursUp);
                    m_ExcludedNeighboursRight = new List<string>(m_SelectedTile.excludedNeighboursRight);
                    m_ExcludedNeighboursLeft = new List<string>(m_SelectedTile.excludedNeighboursLeft);
                    m_ExcludedNeighboursBack = new List<string>(m_SelectedTile.excludedNeighboursDown);
                    m_SocketTypeFront = m_SelectedTile.upSocket.socket_name;
                    m_SocketTypeRight = m_SelectedTile.rightSocket.socket_name;
                    m_SocketTypeLeft = m_SelectedTile.leftSocket.socket_name;
                    m_SocketTypeBack = m_SelectedTile.downSocket.socket_name;
                    m_SocketTypeTop = m_SelectedTile.aboveSocket.socket_name;
                    m_SocketTypeBottom = m_SelectedTile.belowSocket.socket_name;
                    m_SymetricFront = m_SelectedTile.upSocket.isSymmetric;
                    m_SymetricRight = m_SelectedTile.rightSocket.isSymmetric;
                    m_SymetricLeft = m_SelectedTile.leftSocket.isSymmetric;
                    m_SymetricBack = m_SelectedTile.downSocket.isSymmetric;
                    m_FlippedFront = m_SelectedTile.upSocket.isFlipped;
                    m_FlippedRight = m_SelectedTile.rightSocket.isFlipped;
                    m_FlippedLeft = m_SelectedTile.leftSocket.isFlipped;
                    m_FlippedBack = m_SelectedTile.downSocket.isFlipped;
                    m_RotationallyInvariantTop = m_SelectedTile.aboveSocket.rotationallyInvariant;
                    m_RotationallyInvariantBottom = m_SelectedTile.belowSocket.rotationallyInvariant;
                }
            }

            if (updateUI)
            {
                m_TileDropdown.value = tileName;
                m_TileDropdown.choices = m_Tiles
                    .Where(tile => tile.name != "EMPTY" && tile.name != "SOLID")
                    .Select(tile => tile.name)
                    .ToList();
                m_TileDropdown.choices.Add("New tile");
                m_TileNameField.value = m_SelectedTileName;
                m_TileTypeField.value = m_SelectedTileType;
                m_TileProbabilityField.value = m_SelectedTileProbability;
                m_SelectedGameObjectField.value = m_SelectedGameObject;
                m_TileVariation90.value = m_Rotate90;
                m_TileVariation180.value = m_Rotate180;
                m_TileVariation270.value = m_Rotate270;
                m_PositionField.value = m_Position;
                m_RotationField.value = m_Rotation;
                m_ScaleField.value = m_Scale;

                for (int i = 0; i < 4; i++)
                {
                    List<string> excludedNeighboursList = i switch
                    {
                        0 => m_ExcludedNeighboursFront,
                        1 => m_ExcludedNeighboursRight,
                        2 => m_ExcludedNeighboursLeft,
                        _ => m_ExcludedNeighboursBack
                    };

                    List<Toggle> excludedTogglesList = i switch
                    {
                        0 => m_ExcludedNeighboursFrontToggles,
                        1 => m_ExcludedNeighboursRightToggles,
                        2 => m_ExcludedNeighboursLeftToggles,
                        _ => m_ExcludedNeighboursBackToggles
                    };

                    Foldout excludedFoldout = i switch
                    {
                        0 => m_ExcludedNeighboursFrontFoldout,
                        1 => m_ExcludedNeighboursRightFoldout,
                        2 => m_ExcludedNeighboursLeftFoldout,
                        _ => m_ExcludedNeighboursBackFoldout
                    };

                    foreach (Toggle toggle in excludedTogglesList) toggle.RemoveFromHierarchy();
                    excludedTogglesList.Clear();
                    foreach (string tileType in m_SelectedTileset.tileTypes)
                    {
                        Toggle toggle = new Toggle(tileType);
                        toggle.value = excludedNeighboursList.Contains(tileType);
                        toggle.RegisterValueChangedCallback(evt =>
                        {
                            if (evt.newValue) excludedNeighboursList.Add(tileType);
                            else excludedNeighboursList.Remove(tileType);
                        });
                        excludedTogglesList.Add(toggle);
                        excludedFoldout.Add(toggle);
                    }
                }

                m_SocketTypeDropdownFront.value = m_SocketTypeFront;
                m_SocketTypeDropdownFront.choices = m_SocketTypes;
                m_SocketTypeDropdownRight.value = m_SocketTypeRight;
                m_SocketTypeDropdownRight.choices = m_SocketTypes;
                m_SocketTypeDropdownLeft.value = m_SocketTypeLeft;
                m_SocketTypeDropdownLeft.choices = m_SocketTypes;
                m_SocketTypeDropdownBack.value = m_SocketTypeBack;
                m_SocketTypeDropdownBack.choices = m_SocketTypes;
                m_SocketTypeDropdownTop.value = m_SocketTypeTop;
                m_SocketTypeDropdownTop.choices = m_SocketTypes;
                m_SocketTypeDropdownBottom.value = m_SocketTypeBottom;
                m_SocketTypeDropdownBottom.choices = m_SocketTypes;
                m_SymetricFrontToggle.value = m_SymetricFront;
                m_SymetricRightToggle.value = m_SymetricRight;
                m_SymetricLeftToggle.value = m_SymetricLeft;
                m_SymetricBackToggle.value = m_SymetricBack;
                m_FlippedFrontToggle.value = m_FlippedFront;
                m_FlippedRightToggle.value = m_FlippedRight;
                m_FlippedLeftToggle.value = m_FlippedLeft;
                m_FlippedBackToggle.value = m_FlippedBack;
                m_RotationallyInvariantToggleTop.value = m_RotationallyInvariantTop;
                m_RotationallyInvariantToggleBottom.value = m_RotationallyInvariantBottom;
            }
        }

        /// <summary>
        /// Change the selected tile name in the TextField.
        /// This is used to set the tile name for the selected tile.
        /// </summary>
        /// <param name="evt"></param> ChangeEvent with the new tile name
        private void ChangeTileName(ChangeEvent<string> evt)
        {
            m_SelectedTileName = evt.newValue;
        }

        /// <summary>
        /// Change the selected tile type in the TextField.
        /// This is used to set the tile type for the selected tile.
        /// </summary>
        /// <param name="evt"></param>
        private void ChangeTileType(ChangeEvent<string> evt)
        {
            m_SelectedTileType = evt.newValue;
            m_TileTypeField.value = m_SelectedTileType;
        }

        /// <summary>
        /// Change the selected GameObject in the ObjectField.
        /// </summary>
        /// <param name="evt"></param> ChangeEvent with the new GameObject
        private void ChangeGameObject(ChangeEvent<Object> evt)
        {
            m_SelectedGameObject = evt.newValue as GameObject;
            if (m_SelectedGameObjectInstance != null) DestroyImmediate(m_SelectedGameObjectInstance);
            if (m_SelectedGameObject != null)
            {
                m_SelectedGameObjectInstance = Instantiate(m_SelectedGameObject);
                m_SelectedGameObjectInstance.hideFlags = HideFlags.HideAndDontSave;
                m_PreviewRenderUtility.AddSingleGO(m_SelectedGameObjectInstance);
                m_SelectedGameObjectInstanceBounds = GetBounds(m_SelectedGameObjectInstance);
                m_SocketHelperInstance.transform.position = m_SelectedGameObjectInstanceBounds.center;
            }
        }

        /// <summary>
        /// Draw the preview of the selected GameObject in the IMGUIContainer.
        /// This method is called every frame to update the preview.
        /// </summary>
        private void DrawPreview()
        {
            if (m_PreviewRenderUtility == null || m_SelectedGameObjectInstance == null || m_PreviewContainer == null) return;

            Rect rect = m_ImguiContainer.contentRect;
            if (rect.width <= 0 || rect.height <= 0) return;

            Event evt = Event.current;
            int controlID = GUIUtility.GetControlID(FocusType.Passive);

            if (evt.type == EventType.MouseDown && rect.Contains(evt.mousePosition) && evt.button == 0)
            {
                GUIUtility.hotControl = controlID;
                evt.Use();
            }
            else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == controlID)
            {
                m_PreviewDir -= evt.delta * 0.5f;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == controlID)
            {
                GUIUtility.hotControl = 0;
                evt.Use();
            }
            else if (evt.type == EventType.ScrollWheel && rect.Contains(evt.mousePosition))
            {
                m_PreviewDistance += evt.delta.y * 0.2f;
                m_PreviewDistance = Mathf.Clamp(m_PreviewDistance, 2f, 20f);
                evt.Use();
            }

            Quaternion rot = Quaternion.Euler(m_PreviewDir.y, m_PreviewDir.x, 0);
            Vector3 pos = m_SelectedGameObjectInstanceBounds.center + rot * Vector3.back * m_PreviewDistance;

            m_PreviewRenderUtility.camera.transform.position = pos;
            m_PreviewRenderUtility.camera.transform.rotation = rot;

            m_PreviewRenderUtility.BeginPreview(rect, GUIStyle.none);
            m_PreviewRenderUtility.Render(true);
            Texture resultRender = m_PreviewRenderUtility.EndPreview();

            GUI.DrawTexture(rect, resultRender, ScaleMode.ScaleToFit, true);
        }

        /// <summary>
        /// Get the bounds of a GameObject and its children.
        /// This is used to calculate the bounds of the selected game object in the preview.
        /// </summary>
        /// <param name="go"></param> GameObject to get the bounds of
        /// <returns></returns>
        private Bounds GetBounds(GameObject go)
        {
            List<Renderer> renderers = new List<Renderer>();
            Renderer main = go.GetComponent<Renderer>();
            if (main != null) renderers.Add(main);
            renderers.AddRange(go.GetComponentsInChildren<Renderer>());
            if (renderers.Count == 0) return new Bounds(go.transform.position, Vector3.zero);

            Bounds bounds = renderers[0].bounds;
            foreach (Renderer item in renderers) bounds.Encapsulate(item.bounds);
            return bounds;
        }

        private void SaveTile(ClickEvent evt)
        {
            // Check if the selected tilset is null
            if (m_SelectedTileset == null || m_TilesetNameField.value == "Tileset name already exists!") return;

            // Check if the selected tileset name is already in use
            m_Tilesets = Resources.LoadAll<Tileset>("Tilesets/").ToList();
            if (m_Tilesets.Any(tileset => tileset.name == m_SelectedTilesetName) && m_TilesetDropdown.value != m_SelectedTilesetName)
            {
                m_TilesetNameField.value = "Tileset name already exists!";
                return;
            }

            // If the user wants a new tileset, create it and its folders, if not rename the existing one and its tiles folder
            if (m_TilesetDropdown.value == "New tileset")
            {
                AssetDatabase.CreateAsset(m_SelectedTileset, $"Assets/Resources/Tilesets/{m_SelectedTilesetName}.asset");
                AssetDatabase.CreateFolder("Assets/Resources/Tiles", m_SelectedTilesetName);
                m_SelectedTileset = Resources.Load<Tileset>($"Tilesets/{m_SelectedTilesetName}");
            }
            else if (m_SelectedTileset.name != m_SelectedTilesetName)
            {
                AssetDatabase.MoveAsset($"Assets/Resources/Tiles/{m_SelectedTileset.name}", $"Assets/Resources/Tiles/{m_SelectedTilesetName}");
                AssetDatabase.RenameAsset($"Assets/Resources/Tilesets/{m_SelectedTileset.name}.asset", m_SelectedTilesetName);
                m_SelectedTileset = Resources.Load<Tileset>($"Tilesets/{m_SelectedTilesetName}");
            }

            // Save the tileset
            m_SelectedTileset.tileSize = m_TileSize;
            m_SelectedTileset.socketTypes = m_SocketTypes;
            m_SelectedTileset.tiles = m_Tiles;
            m_SelectedTileset.tileCount = m_Tiles.Count;
            m_SelectedTileset.tileTypes = m_Tiles.Select(tile => tile.tileType).Where(tileType => tileType != "").ToList();
            // Update the lists
            m_TilesetDropdown.choices = m_Tilesets
                .Select(tileset => tileset.name)
                .ToList();
            m_TilesetDropdown.choices.Add("New tileset");
            m_TilesetDropdown.value = m_SelectedTilesetName;

            m_SocketTypes = m_SelectedTileset.socketTypes;
            m_SocketTypeDropdownField.choices = m_SocketTypes.Where(socketType => socketType != "Empty" && socketType != "Solid").ToList();
            m_SocketTypeDropdownField.value = "";
            m_SocketTypeNameField.value = "";

            // Check if the selected prefab is null
            if (m_SelectedGameObject == null)
            {
                m_SelectedGameObjectField.Focus();
                return;
            }

            // Check if the selected tile name is already in use
            if (!AssetDatabase.IsValidFolder($"Assets/Resources/Tiles")) AssetDatabase.CreateFolder("Assets/Resources", "Tiles");
            if (m_TileDropdown.value == "New tile")
            {
                if (AssetDatabase.IsValidFolder($"Assets/Resources/Tiles/{m_SelectedTilesetName}"))
                {
                    List<Tile3D> tiles = Resources.LoadAll<Tile3D>($"Tilesets/{m_SelectedTilesetName}/").ToList();
                    if (tiles.Any(tile => tile.name == m_SelectedTileName))
                    {
                        m_TileNameField.value = "Tileset name already exists!";
                        return;
                    }
                }
                else AssetDatabase.CreateFolder($"Assets/Resources/Tiles", m_SelectedTilesetName);
                Tile3D possibleTrash = m_SelectedGameObject.GetComponent<Tile3D>();
                if (possibleTrash != null) DestroyImmediate(m_SelectedGameObject.GetComponent<Tile3D>(), true);
                m_SelectedTile = m_SelectedGameObject.AddComponent<Tile3D>();
            }

            // Set all the values of the selected tile
            m_SelectedTile.name = m_SelectedTileName;
            m_SelectedTile.tileType = m_SelectedTileType;
            m_SelectedTile.probability = m_SelectedTileProbability;
            m_SelectedTile.rotateRight = m_Rotate90;
            m_SelectedTile.rotate180 = m_Rotate180;
            m_SelectedTile.rotateLeft = m_Rotate270;
            m_SelectedTile.positionOffset = m_Position;
            m_SelectedTile.rotation = m_Rotation;
            m_SelectedTile.scale = m_Scale;
            m_SelectedTile.excludedNeighboursUp = m_ExcludedNeighboursFront;
            m_SelectedTile.excludedNeighboursRight = m_ExcludedNeighboursRight;
            m_SelectedTile.excludedNeighboursLeft = m_ExcludedNeighboursLeft;
            m_SelectedTile.excludedNeighboursDown = m_ExcludedNeighboursBack;
            m_SelectedTile.upSocket.socket_name = m_SocketTypeFront;
            m_SelectedTile.upSocket.horizontalFace = true;
            m_SelectedTile.upSocket.isSymmetric = m_SymetricFront;
            m_SelectedTile.upSocket.isFlipped = m_FlippedFront;
            m_SelectedTile.rightSocket.socket_name = m_SocketTypeRight;
            m_SelectedTile.rightSocket.horizontalFace = true;
            m_SelectedTile.rightSocket.isSymmetric = m_SymetricRight;
            m_SelectedTile.rightSocket.isFlipped = m_FlippedRight;
            m_SelectedTile.downSocket.socket_name = m_SocketTypeBack;
            m_SelectedTile.downSocket.horizontalFace = true;
            m_SelectedTile.downSocket.isSymmetric = m_SymetricBack;
            m_SelectedTile.downSocket.isFlipped = m_FlippedBack;
            m_SelectedTile.leftSocket.socket_name = m_SocketTypeLeft;
            m_SelectedTile.leftSocket.horizontalFace = true;
            m_SelectedTile.leftSocket.isSymmetric = m_SymetricLeft;
            m_SelectedTile.leftSocket.isFlipped = m_FlippedLeft;
            m_SelectedTile.aboveSocket.socket_name = m_SocketTypeTop;
            m_SelectedTile.aboveSocket.verticalFace = true;
            m_SelectedTile.aboveSocket.rotationallyInvariant = m_RotationallyInvariantTop;
            m_SelectedTile.aboveSocket.rotationIndex = 0;
            m_SelectedTile.belowSocket.socket_name = m_SocketTypeBottom;
            m_SelectedTile.belowSocket.verticalFace = true;
            m_SelectedTile.belowSocket.rotationallyInvariant = m_RotationallyInvariantBottom;
            m_SelectedTile.belowSocket.rotationIndex = 0;
            m_SelectedTile.gameObject.name = m_SelectedTileName;

            // Move the asset to the new folder
            AssetDatabase.MoveAsset(AssetDatabase.GetAssetPath(m_SelectedTile), $"Assets/Resources/Tiles/{m_SelectedTilesetName}/{m_SelectedTileName}.prefab");
            if(!m_SelectedTileset.tiles.Contains(m_SelectedTile)) m_SelectedTileset.tiles.Add(m_SelectedTile);
            if(!m_SelectedTileset.tiles.Contains(m_EmptyTile)) m_SelectedTileset.tiles.Add(m_EmptyTile);
            if(!m_SelectedTileset.tiles.Contains(m_SolidTile)) m_SelectedTileset.tiles.Add(m_SolidTile);
            m_SelectedTileset.tileCount = m_SelectedTileset.tiles.Count;
            m_SelectedTileset.tileTypes = m_SelectedTileset.tiles.Select(tile => tile.tileType).Where(tileType => tileType != "").Distinct().ToList();
            ChangeTile(m_SelectedTileName);
            if(m_SelectedTileset != null) EditorUtility.SetDirty(m_SelectedTileset);
            if(m_SelectedTile != null) EditorUtility.SetDirty(m_SelectedTile);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}