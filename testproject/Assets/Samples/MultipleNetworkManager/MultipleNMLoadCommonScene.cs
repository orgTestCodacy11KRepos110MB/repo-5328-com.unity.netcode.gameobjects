using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MultipleNMLoadCommonScene : MonoBehaviour
{
    public SceneAsset CommonScene;

    // Start is called before the first frame update
    private void Start()
    {
        if (CommonScene == null)
        {
            Debug.LogError("Cannot load common scene. Scene not set.");
            return;
        }
        var nm = GetComponent<NetworkManager>();
        nm.OnServerStarted += () => nm.SceneManager.LoadScene(CommonScene.name, LoadSceneMode.Single);
    }
}
