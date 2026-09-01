using UnityEngine;

public class Billboard : MonoBehaviour
{
    public Sprite[] frames;
    public int frameIndex;
    public Sprite sprite;

    public bool faceCamera = true;
    public float heightScale = 1f;

    private SpriteRenderer sr;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        ApplySprite();
    }

    private void Update()
    {
        if (faceCamera && Camera.main != null)
            transform.rotation = Camera.main.transform.rotation;
    }

    public void ApplySprite()
    {
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        if (sr == null) return;
        if (sprite != null) sr.sprite = sprite;
        else if (frames != null && frames.Length > 0)
            sr.sprite = frames[Mathf.Clamp(frameIndex, 0, frames.Length - 1)];
        if (sr.sprite != null)
        {
            var b = sr.sprite.bounds.size;
            float s = b.y > 0.001f ? heightScale / b.y : 1f;
            transform.localScale = new Vector3(s, s, 1f);
        }
    }

    public void SetFrame(int i)
    {
        frameIndex = i;
        ApplySprite();
    }
}
