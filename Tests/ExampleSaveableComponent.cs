using UnityEngine;

public class ExampleSaveableComponent : MonoBehaviour
{
    [Header("Persistent Identification")]
    [SerializeField] private PersistentId persistentId = new PersistentId();

    [Header("Saveable Data")]
    [SerializeField] private int score = 0;
    [SerializeField] private string playerName = "Player";
    [SerializeField] private Vector3 savedPosition;

    public PersistentId PersistentId => persistentId;
    public int Score { get => score; set => score = value; }
    public string PlayerName { get => playerName; set => playerName = value; }
    public Vector3 SavedPosition { get => savedPosition; set => savedPosition = value; }

    private void Start()
    {
        // Example: Save current position on start
        savedPosition = transform.position;
    }

    private void Update()
    {
        // Example: Update saved position if object moves
        if(Vector3.Distance(transform.position, savedPosition) > 0.1f)
        {
            savedPosition = transform.position;
        }
    }

    // Example save data structure
    [System.Serializable]
    public class SaveData
    {
        public int score;
        public string playerName;
        public Vector3 position;

        public SaveData(ExampleSaveableComponent component)
        {
            score = component.Score;
            playerName = component.PlayerName;
            position = component.SavedPosition;
        }
    }

    public SaveData GetSaveData()
    {
        return new SaveData(this);
    }

    public void LoadSaveData(SaveData data)
    {
        Score = data.score;
        PlayerName = data.playerName;
        SavedPosition = data.position;
        transform.position = data.position;
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Tools/Test PersistentId")]
    private static void TestPersistentId()
    {
        var selected = UnityEditor.Selection.activeGameObject;
        if(selected != null)
        {
            var component = selected.GetComponent<ExampleSaveableComponent>();
            if(component != null)
            {
                Debug.Log($"PersistentId: {component.persistentId}");
                Debug.Log($"Is Valid: {component.persistentId.IsValid}");
            }
        }
    }
#endif
}