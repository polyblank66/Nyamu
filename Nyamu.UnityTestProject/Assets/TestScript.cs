using UnityEngine;

public class TestScript : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        new GameObject("Test").AddComponent<TestModuleScript>();
    }

    // Update is called once per frame
    void Update()
    {
    }
}
