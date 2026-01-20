using UnityEngine;

public class ProgressAnimation : MonoBehaviour
{
    RectTransform rectTransform;
    Vector3 rotation = new Vector3(0, 0, 0);
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    // Update is called once per frame
    void Update()
    {
        rotation += new Vector3(0, 0, 200) * Time.deltaTime;
        if(rotation.z >= 360)
        {
            rotation.z = 0;
        }
        rectTransform.localEulerAngles = rotation;
    }
}
