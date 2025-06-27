using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Linq;

namespace WFC3DMapGenerator
{
    public class WFC_Generation_Editor : EditorWindow
    {
        // UI elements
        [SerializeField] private VisualTreeAsset m_UXML;
        [SerializeField] private RadioButtonGroup m_GenerationStrategy;
        [SerializeField] private DropdownField m_TilesetDropdown;
        [SerializeField] private DropdownField m_MapDropdown;
        [SerializeField] private VisualElement m_MapSizeSliderContainer;
        [SerializeField] private SliderInt m_MapSizeX;
        [SerializeField] private SliderInt m_MapSizeY;
        [SerializeField] private SliderInt m_MapSizeZ;
        [SerializeField] private Vector3IntField m_MapSizeField;
        [SerializeField] private VisualElement m_VisualElementWarning;
        [SerializeField] private VisualElement m_VisualElementTilesetWarning;
        [SerializeField] private ProgressBar m_ProgressBar;
        [SerializeField] private Button m_GenerateButton;

        // Internal variables
        [HideInInspector] [SerializeField] private bool m_UseChunks;
        [HideInInspector] [SerializeField] private Tileset m_Tileset;
        [SerializeField] private GameObject m_WFCPrefab;
        [HideInInspector] [SerializeField] private GameObject m_SelectedMap;
        [HideInInspector] [SerializeField] private Vector3Int m_MapSize = new Vector3Int(1, 1, 1);
        [HideInInspector] [SerializeField] private bool m_GeneratingMap = false;
        [HideInInspector] [SerializeField] private int m_GenerationProgress;

        [MenuItem("Tools/WFC Generation/WFC Map Generator")]
        public static void ShowWindow()
        {
            WFC_Generation_Editor window = GetWindow<WFC_Generation_Editor>();
            window.titleContent = new GUIContent("WFC Map Generator");
        }

        /// <summary>
        /// Called when the editor window is created.
        /// This method initializes the UI elements and sets up the callbacks for user interactions.
        /// </summary>
        private void CreateGUI()
        {
            // Load the UXML and instantiate it
            VisualElement root = rootVisualElement;
            m_UXML.CloneTree(root);
            m_UseChunks = true;
            m_MapSize = new Vector3Int(4, 3, 4);

            // Check if there's a generation in progress and set the selected map accordingly
            WaveFunction3DGPUChunks[] tempSearch = FindObjectsByType<WaveFunction3DGPUChunks>(FindObjectsSortMode.None);
            foreach (WaveFunction3DGPUChunks wfc in tempSearch)
            {
                if (!wfc.IsFinished())
                {
                    m_GeneratingMap = true;
                    m_SelectedMap = wfc.gameObject;
                }
                else if (!wfc.GetComponent<WaveFunction3DGPU>().IsFinished())
                {
                    m_GeneratingMap = true;
                    m_SelectedMap = wfc.gameObject;
                    m_UseChunks = false;
                }
            }

            // Load data for the generation strategy radio buttons (uses chunk by default)
            m_GenerationStrategy = rootVisualElement.Q<RadioButtonGroup>("strategySelector");
            if (m_GenerationStrategy != null)
            {
                m_GenerationStrategy.value = 0;
                m_UseChunks = true;
                m_GenerationStrategy.RegisterValueChangedCallback(SetGenerationStrategy);
            }

            // Load data for the tileset dropdown
            m_TilesetDropdown = rootVisualElement.Q<DropdownField>("tilesetSelector");
            if (m_TilesetDropdown != null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources/Tilesets")) AssetDatabase.CreateFolder("Assets/Resources", "Tilesets");
                m_TilesetDropdown.choices = Resources.LoadAll<Tileset>("Tilesets/")
                        .Select(asset => asset.name)
                        .ToList();
                if (m_TilesetDropdown.choices.Count == 0)
                {
                    m_TilesetDropdown.value = "No tilesets found";
                    m_Tileset = null;
                }
                else
                {
                    m_Tileset = Resources.Load<Tileset>($"Tilesets/{m_TilesetDropdown.choices[0]}");
                    if (m_Tileset != null) m_TilesetDropdown.value = m_Tileset.name;
                }
                m_TilesetDropdown.RegisterValueChangedCallback(SetTileSet);
            }

            // Load data for the map selector dropdown (new map always selected by default)
            m_MapDropdown = rootVisualElement.Q<DropdownField>("mapSelector");
            if (m_MapDropdown != null)
            {
                m_MapDropdown.choices = FindObjectsByType<WaveFunction3DGPUChunks>(FindObjectsSortMode.None)
                .Select(wfc => wfc.name)
                .ToList();
                m_MapDropdown.choices.Add("New map");
                m_MapDropdown.value = m_MapDropdown.choices[^1];
                m_MapDropdown.RegisterValueChangedCallback(SetMap);
            }

            // Load data for the map size sliders
            m_MapSizeSliderContainer = rootVisualElement.Q<VisualElement>("mapDimensionsSliderContainer");
            m_MapSizeX = rootVisualElement.Q<SliderInt>("mapDimensionsX");
            m_MapSizeY = rootVisualElement.Q<SliderInt>("mapDimensionsY");
            m_MapSizeZ = rootVisualElement.Q<SliderInt>("mapDimensionsZ");
            if (m_MapSizeX != null) m_MapSizeX.RegisterValueChangedCallback(evt => SetMapSize(evt, 'x'));
            if (m_MapSizeY != null) m_MapSizeY.RegisterValueChangedCallback(evt => SetMapSize(evt, 'y'));
            if (m_MapSizeZ != null) m_MapSizeZ.RegisterValueChangedCallback(evt => SetMapSize(evt, 'z'));
            if (m_MapSizeSliderContainer != null && !m_UseChunks) m_MapSizeSliderContainer.style.display = DisplayStyle.Flex;
            else if (m_MapSizeSliderContainer != null && m_UseChunks) m_MapSizeSliderContainer.style.display = DisplayStyle.None;

            // Load data for the map size field
            m_MapSizeField = rootVisualElement.Q<Vector3IntField>("mapDimensions");
            if (m_MapSizeField != null)
            {
                m_MapSizeField.RegisterValueChangedCallback(SetMapSize);
                if (m_UseChunks) m_MapSizeField.style.display = DisplayStyle.Flex;
                else m_MapSizeField.style.display = DisplayStyle.None;
            }

            // Load data to show or hide the warning label
            m_VisualElementWarning = rootVisualElement.Q<VisualElement>("generationWarning");
            if (m_VisualElementWarning != null)
            {
                if (m_GeneratingMap) m_VisualElementWarning.style.display = DisplayStyle.Flex;
                else m_VisualElementWarning.style.display = DisplayStyle.None;
            }

            m_VisualElementTilesetWarning = rootVisualElement.Q<VisualElement>("tilesetWarning");
            if (m_VisualElementTilesetWarning != null) m_VisualElementTilesetWarning.style.display = DisplayStyle.None;

            // Load data for the progress bar
            m_ProgressBar = rootVisualElement.Q<ProgressBar>("generationProgress");
            if (m_ProgressBar != null)
            {
                if (m_GeneratingMap)
                {
                    m_ProgressBar.style.display = DisplayStyle.Flex;
                    if (m_UseChunks)
                    {
                        WaveFunction3DGPUChunks wfc = m_SelectedMap.GetComponent<WaveFunction3DGPUChunks>();
                        if (wfc != null) m_ProgressBar.value = wfc.GetProgress();
                    }
                    else m_ProgressBar.value = 0; //There's no way to get the progress of the parallel generation yet
                }
                else m_ProgressBar.style.display = DisplayStyle.None;
            }

            m_GenerateButton = rootVisualElement.Q<Button>("generationButton");
            if (m_GenerateButton != null)
            {
                if (m_GeneratingMap)
                {
                    m_GenerateButton.text = "Stop generation";
                    m_GenerateButton.UnregisterCallback<ClickEvent>(StartGeneration);
                    m_GenerateButton.RegisterCallback<ClickEvent>(StopGeneration);
                }
                else
                {
                    m_GenerateButton.UnregisterCallback<ClickEvent>(StopGeneration);
                    m_GenerateButton.RegisterCallback<ClickEvent>(StartGeneration);
                }
            }
        }

        /// <summary>
        /// Called when the inspector is focused.
        /// This method updates the map and tileset dropdowns with the available options.
        /// It also registers callbacks for the dropdown value changes.
        /// </summary>
        private void OnFocus()
        {
            if (m_MapDropdown != null)
            {
                m_MapDropdown.choices = FindObjectsByType<WaveFunction3DGPUChunks>(FindObjectsSortMode.None)
                    .Select(wfc => wfc.name)
                    .ToList();
                m_MapDropdown.choices.Add("New map");
                m_MapDropdown.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue == "New map") m_SelectedMap = null;
                    else m_SelectedMap = FindObjectsByType<WaveFunction3DGPUChunks>(FindObjectsSortMode.None)
                                        .FirstOrDefault(wfc => wfc.name == evt.newValue)?.gameObject;
                });
            }

            if (m_TilesetDropdown != null)
            {
                m_TilesetDropdown.choices = Resources.LoadAll<Tileset>("Tilesets/")
                    .Select(asset => asset.name)
                    .ToList();
                if (m_TilesetDropdown.choices.Count == 0)
                {
                    m_TilesetDropdown.value = "No tilesets found";
                    m_Tileset = null;
                }
            }
        }

        /// <summary>
        /// Called when the inspector is updated.
        /// This method checks if the generation is finished and updates the progress bar accordingly.
        /// </summary>
        private void OnInspectorUpdate()
        {
            if (m_GeneratingMap && m_SelectedMap == null)
            {
                GenerationFinished();
                return;
            }

            if (m_GeneratingMap && m_ProgressBar != null)
            {
                if (m_UseChunks)
                {
                    WaveFunction3DGPUChunks wfc = m_SelectedMap.GetComponent<WaveFunction3DGPUChunks>();
                    if (wfc != null && wfc.IsFinished()) GenerationFinished();
                    else if (wfc != null)
                    {
                        m_GenerationProgress = wfc.GetProgress();
                        m_ProgressBar.value = m_GenerationProgress;
                    }
                }
                else
                {
                    WaveFunction3DGPU wfc = m_SelectedMap.GetComponent<WaveFunction3DGPU>();
                    if (wfc != null && wfc.IsFinished()) GenerationFinished();
                    else if (wfc != null) m_ProgressBar.value = 0; //There's no way to get the progress of the parallel generation yet
                }
            }
        }


        /// <summary>
        /// Called when the user changes the value of the generation strategy radio buttons.
        /// This method updates the UI elements to reflect the selected generation strategy.
        /// </summary>
        /// <param name="evt"></param>
        private void SetGenerationStrategy(ChangeEvent<int> evt)
        {
            m_UseChunks = evt.newValue == 0;
            if (m_MapSizeSliderContainer != null)
            {
                if (m_UseChunks) m_MapSizeSliderContainer.style.display = DisplayStyle.None;
                else
                {
                    m_MapSizeX.value = m_MapSize.x;
                    m_MapSizeY.value = m_MapSize.y - 2;
                    m_MapSizeZ.value = m_MapSize.z;
                    m_MapSizeSliderContainer.style.display = DisplayStyle.Flex;
                }
            }
            if (m_MapSizeField != null)
            {
                if (m_UseChunks)
                {
                    m_MapSizeField.value = new Vector3Int(m_MapSize.x, m_MapSize.y - 2, m_MapSize.z);
                    m_MapSizeField.style.display = DisplayStyle.Flex;
                }
                else m_MapSizeField.style.display = DisplayStyle.None;
            }
        }

        /// <summary>
        /// Called when the user changes the value of the tileset selector dropdown.
        /// This method updates the selected tileset based on the user's choice.
        /// </summary>
        /// <param name="evt"></param>
        private void SetTileSet(ChangeEvent<string> evt)
        {
            m_Tileset = AssetDatabase.LoadAssetAtPath<Tileset>($"Assets/Resources/Tilesets/{evt.newValue}.asset");
            if (m_Tileset != null && m_VisualElementTilesetWarning != null) m_VisualElementTilesetWarning.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// Called when the user changes the value of the map selector dropdown.
        /// This method updates the selected map based on the user's choice.
        /// </summary>
        /// <param name="evt"></param>
        private void SetMap(ChangeEvent<string> evt)
        {
            if (evt.newValue == "New map") m_SelectedMap = null;
            else m_SelectedMap = FindObjectsByType<WaveFunction3DGPUChunks>(FindObjectsSortMode.None)
                                .FirstOrDefault(wfc => wfc.name == evt.newValue)?.gameObject;
        }

        /// <summary>
        /// Called when the user changes the value of the map size field.
        /// This method updates the map size vector and ensures that the values are within valid ranges.
        /// The y coordinate is adjusted to be at least 2 units larger than the minimum value. (The algorithm needs a minimum of 2 extra units of height)
        /// </summary>
        /// <param name="evt"></param>
        private void SetMapSize(ChangeEvent<Vector3Int> evt)
        {
            m_MapSize = new Vector3Int(evt.newValue.x, evt.newValue.y, evt.newValue.z);
            if (m_MapSize.x < 4) m_MapSize.x = 4;
            if (m_MapSize.y < 1) m_MapSize.y = 1;
            if (m_MapSize.z < 4) m_MapSize.z = 4;
            if (m_MapSize != null) m_MapSizeField.value = m_MapSize;
            m_MapSize.y += 2;
        }

        /// <summary>
        /// Called when the user changes the value of the map size sliders.
        /// This method updates the corresponding coordinate of the map size vector.
        /// </summary>
        /// <param name="evt"></param>
        /// <param name="coordinate"></param>
        private void SetMapSize(ChangeEvent<int> evt, char coordinate)
        {
            if (coordinate == 'x') m_MapSize.x = evt.newValue;
            else if (coordinate == 'y') m_MapSize.y = evt.newValue + 2;
            else if (coordinate == 'z') m_MapSize.z = evt.newValue;
        }

        /// <summary>
        /// Called when the user clicks the "Start generation" button.
        /// This method starts the generation process of the WFC map.
        /// </summary>
        /// <param name="evt"></param>
        private void StartGeneration(ClickEvent evt)
        {
            if (m_Tileset == null)
            {
                if (m_VisualElementTilesetWarning != null) m_VisualElementTilesetWarning.style.display = DisplayStyle.Flex;
                return;
            }

            m_GeneratingMap = true;
            m_GenerationProgress = 0;
            if (m_TilesetDropdown != null) m_TilesetDropdown.SetEnabled(false);
            if (m_MapDropdown != null) m_MapDropdown.SetEnabled(false);
            if (m_ProgressBar != null) m_ProgressBar.value = 0;
            if (m_VisualElementWarning != null) m_VisualElementWarning.style.display = DisplayStyle.Flex;
            if (m_ProgressBar != null) m_ProgressBar.style.display = DisplayStyle.Flex;
            if (m_GenerateButton != null)
            {
                m_GenerateButton.text = "Stop generation";
                m_GenerateButton.UnregisterCallback<ClickEvent>(StartGeneration);
                m_GenerateButton.RegisterCallback<ClickEvent>(StopGeneration);
            }

            if (m_SelectedMap == null) m_SelectedMap = Instantiate(m_WFCPrefab, Vector3.zero, Quaternion.identity);
            if (m_MapDropdown != null) m_MapDropdown.value = m_SelectedMap.name;

            if (m_UseChunks)
            {
                WaveFunction3DGPUChunks wfc = m_SelectedMap.GetComponent<WaveFunction3DGPUChunks>();
                if (wfc == null) wfc = m_SelectedMap.AddComponent<WaveFunction3DGPUChunks>();
                wfc.Initialize(m_MapSize, m_Tileset.tileSize, m_Tileset.tiles.ToArray());
            }
            else
            {
                WaveFunction3DGPU wfc = m_SelectedMap.GetComponent<WaveFunction3DGPU>();
                if (wfc == null) wfc = m_SelectedMap.AddComponent<WaveFunction3DGPU>();
                wfc.Initialize(m_MapSize, m_Tileset.tileSize, m_Tileset.tiles.ToArray());
            }
        }

        /// <summary>
        /// Called when the user clicks the "Stop generation" button.
        /// This method stops the generation process of the WFC map.
        /// </summary>
        /// <param name="evt"></param>
        private void StopGeneration(ClickEvent evt)
        {
            if (m_UseChunks && m_SelectedMap != null)
            {
                WaveFunction3DGPUChunks wfc = m_SelectedMap.GetComponent<WaveFunction3DGPUChunks>();
                if (wfc != null) wfc.StopGeneration();
            }
            else if (!m_UseChunks && m_SelectedMap != null)
            {
                WaveFunction3DGPU wfc = m_SelectedMap.GetComponent<WaveFunction3DGPU>();
                if (wfc != null) wfc.StopGeneration();
            }
        }

        /// <summary>
        /// Called when the generation is finished.
        /// This method updates the UI elements to reflect the end of the generation process.
        /// </summary>
        private void GenerationFinished()
        {
            m_GeneratingMap = false;
            if (m_TilesetDropdown != null) m_TilesetDropdown.SetEnabled(true);
            if (m_MapDropdown != null) m_MapDropdown.SetEnabled(true);
            if (m_VisualElementWarning != null) m_VisualElementWarning.style.display = DisplayStyle.None;
            if (m_ProgressBar != null) m_ProgressBar.style.display = DisplayStyle.None;
            if (m_GenerateButton != null)
            {
                m_GenerateButton.text = "Start generation";
                m_GenerateButton.UnregisterCallback<ClickEvent>(StopGeneration);
                m_GenerateButton.RegisterCallback<ClickEvent>(StartGeneration);
            }
        }
    }
}
