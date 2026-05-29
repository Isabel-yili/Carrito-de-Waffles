using UnityEngine;

public class PlateSpawner : MonoBehaviour
{
    public static PlateSpawner Instance;

    [Header("Setup")]
    public GameObject platePrefab;

    public Transform spawnPoint;

    private void Awake()
    {
        Instance = this;
    }

    public void SpawnPlate()
    {
        Instantiate(
            platePrefab,
            spawnPoint.position,
            Quaternion.identity,
            spawnPoint.parent
        );
    }
}