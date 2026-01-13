using UnityEngine;

public class FreezeUIController : MonoBehaviour
{
    public GameObject grayIcon;
    public GameObject blueIcon;

    public void SetFreezeActive(bool active)
    {
        grayIcon.SetActive(!active);
        blueIcon.SetActive(active);
    }
}
