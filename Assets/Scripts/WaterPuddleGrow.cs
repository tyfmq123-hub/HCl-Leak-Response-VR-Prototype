using System.Collections;
using UnityEngine;

/// <summary>
/// 플랜지 누출이 진행되는 동안
/// 바닥의 물웅덩이를 천천히 넓히는 스크립트입니다.
///
/// 누출을 멈추더라도 이미 고인 물은 그대로 남습니다.
/// 물웅덩이를 완전히 없애려면 ResetPuddle()을 호출합니다.
/// </summary>
public class WaterPuddleGrow : MonoBehaviour
{
    [Header("물웅덩이 오브젝트")]

    [Tooltip("바닥에 만든 Puddle 오브젝트를 연결합니다.")]
    [SerializeField]
    private GameObject puddleObject;


    [Header("성장 설정")]

    [Tooltip("물웅덩이가 최대 크기에 도달하는 시간입니다.")]
    [SerializeField]
    private float growthDuration = 15f;

    [Tooltip("처음 크기에 비해 최대로 커지는 배율입니다.")]
    [SerializeField]
    private float maximumScaleMultiplier = 6f;

    [Tooltip("게임 시작과 동시에 물웅덩이를 키울지 결정합니다.")]
    [SerializeField]
    private bool playOnStart = true;


    // Unity에서 직접 맞춘 Puddle의 최초 크기를 기억합니다.
    private Vector3 initialScale;

    // 물웅덩이의 최대 크기입니다.
    private Vector3 maximumScale;

    // 현재 실행 중인 성장 코루틴입니다.
    private Coroutine growthCoroutine;

    // 지금까지 물웅덩이가 얼마나 성장했는지 기억합니다.
    // 누출을 멈췄다가 다시 시작해도 처음부터 되돌아가지 않습니다.
    private float currentGrowthTime;


    private void Awake()
    {
        if (puddleObject == null)
        {
            Debug.LogError(
                "[WaterPuddleGrow] Puddle Object가 연결되지 않았습니다.",
                this
            );

            return;
        }

        initialScale = puddleObject.transform.localScale;

        /*
         * 물웅덩이는 바닥 방향인 X와 Z만 넓어집니다.
         *
         * Y까지 커지면 물웅덩이가 위로 두꺼워져
         * 젤리나 원기둥처럼 보이므로 Y 크기는 그대로 유지합니다.
         */
        maximumScale = new Vector3(
            initialScale.x * maximumScaleMultiplier,
            initialScale.y,
            initialScale.z * maximumScaleMultiplier
        );

        // 시작 전에는 물웅덩이가 보이지 않게 합니다.
        puddleObject.SetActive(false);
    }


    private void Start()
    {
        if (playOnStart)
        {
            StartLeak();
        }
    }


    /// <summary>
    /// 누출을 시작하고 물웅덩이를 계속 키웁니다.
    /// </summary>
    public void StartLeak()
    {
        if (puddleObject == null)
        {
            return;
        }

        puddleObject.SetActive(true);

        // 이미 성장 중이면 코루틴을 중복 실행하지 않습니다.
        if (growthCoroutine != null)
        {
            return;
        }

        // 이미 최대 크기에 도달했다면 더 이상 키우지 않습니다.
        if (currentGrowthTime >= growthDuration)
        {
            puddleObject.transform.localScale = maximumScale;
            return;
        }

        growthCoroutine = StartCoroutine(GrowPuddle());
    }


    /// <summary>
    /// 누출을 멈추고 성장을 일시정지합니다.
    ///
    /// 실제 현장에서도 누출을 막았다고 이미 고인 물이
    /// 바로 사라지지는 않으므로 물웅덩이는 그대로 남깁니다.
    /// </summary>
    public void StopLeak()
    {
        if (growthCoroutine == null)
        {
            return;
        }

        StopCoroutine(growthCoroutine);
        growthCoroutine = null;
    }


    /// <summary>
    /// 물웅덩이를 최초 상태로 되돌리고 숨깁니다.
    /// Scene을 다시 시작하거나 훈련을 초기화할 때 사용할 수 있습니다.
    /// </summary>
    public void ResetPuddle()
    {
        StopLeak();

        currentGrowthTime = 0f;

        if (puddleObject == null)
        {
            return;
        }

        puddleObject.transform.localScale = initialScale;
        puddleObject.SetActive(false);
    }


    private IEnumerator GrowPuddle()
    {
        while (currentGrowthTime < growthDuration)
        {
            currentGrowthTime += Time.deltaTime;

            float growthRatio = Mathf.Clamp01(
                currentGrowthTime / growthDuration
            );

            /*
             * SmoothStep을 사용하면 일정한 속도로 기계적으로 커지지 않고,
             * 처음과 마지막이 조금 부드럽게 변화합니다.
             */
            float smoothGrowthRatio = Mathf.SmoothStep(
                0f,
                1f,
                growthRatio
            );

            puddleObject.transform.localScale = Vector3.Lerp(
                initialScale,
                maximumScale,
                smoothGrowthRatio
            );

            yield return null;
        }

        puddleObject.transform.localScale = maximumScale;
        growthCoroutine = null;
    }


    private void OnDisable()
    {
        if (growthCoroutine != null)
        {
            StopCoroutine(growthCoroutine);
            growthCoroutine = null;
        }
    }
}