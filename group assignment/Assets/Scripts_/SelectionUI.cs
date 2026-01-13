using UnityEngine;

public class SelectionUIController : MonoBehaviour
{
    [Header("Cube Slots (in order)")]
    public GameObject[] whiteIcons;
    public GameObject[] orangeIcons;

    public void SetSelectedCount(int count)
    {
        for (int i = 0; i < whiteIcons.Length; i++)
        {
            bool active = i < count;

            whiteIcons[i].SetActive(!active);
            orangeIcons[i].SetActive(active);
        }
    }
}
