using UnityEngine;

public class ScreenPosMarker : MonoBehaviour
{
    
}

#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(ScreenPosMarker))]
public class ScreenPosMarkerEditor:UnityEditor.Editor
{
    private ScreenPosMarker _target;
    private void OnEnable()
    {
        _target = target as ScreenPosMarker;
    }

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if (_target != null && Camera.main != null)
        {
            UnityEditor.EditorGUILayout.Vector3Field("ScreenPos:", Camera.main.WorldToViewportPoint(_target.transform.position));
        }
    }
}

#endif
