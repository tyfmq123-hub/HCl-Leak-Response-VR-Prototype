using UnityEngine;

public class ProcedureHighlight : MonoBehaviour
{
    [Header("하이라이트 설정")]
    [SerializeField] private Color highlightColor = Color.cyan;
    [SerializeField] private float minimumIntensity = 0.3f;
    [SerializeField] private float maximumIntensity = 3f;
    [SerializeField] private float pulseSpeed = 2f;

    private static readonly int EmissionColorId =
        Shader.PropertyToID("_EmissionColor");

    private Renderer targetRenderer;
    private MaterialPropertyBlock propertyBlock;
    private bool isHighlighted;

    private void Awake()
    {
        // 이 스크립트가 붙은 오브젝트의 Renderer를 찾습니다.
        targetRenderer = GetComponent<Renderer>();

        propertyBlock = new MaterialPropertyBlock();

        // 시작할 때는 하이라이트를 끕니다.
        HideHighlight();
    }

    private void Update()
    {
        if (!isHighlighted || targetRenderer == null)
        {
            return;
        }

        // 0~1 사이를 반복하는 값입니다.
        float pulse =
            (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f;

        // 최소 밝기와 최대 밝기 사이를 반복합니다.
        float intensity =
            Mathf.Lerp(minimumIntensity, maximumIntensity, pulse);

        ApplyEmission(highlightColor * intensity);
    }

    public void ShowHighlight()
    {
        isHighlighted = true;
    }

    public void HideHighlight()
    {
        isHighlighted = false;
        ApplyEmission(Color.black);
    }

    private void ApplyEmission(Color emissionColor)
    {
        if (targetRenderer == null)
        {
            return;
        }

        targetRenderer.GetPropertyBlock(propertyBlock);

        propertyBlock.SetColor(
            EmissionColorId,
            emissionColor
        );

        targetRenderer.SetPropertyBlock(propertyBlock);
    }
    private void OnEnable()
    {
        // Enabling an object is not an explicit procedure highlight request.
        // Procedure steps can still call ShowHighlight when emphasis is required.
        HideHighlight();
    }
}
