using System.Collections;
using UnityEngine;

/// <summary>
/// 정적인 물튀김 3D 모델을 짧게 켰다가 끄면서
/// 물방울이 바닥에 부딪히는 것처럼 반복 재생합니다.
///
/// 이 스크립트는 활성 상태인 FloorImpactPoint에 붙이고,
/// SplashBurst 모델은 평소 비활성 상태로 둡니다.
/// </summary>
public class WaterSplashBurst : MonoBehaviour
{
    [Header("물튀김 모델")]

    [Tooltip("바닥에 배치한 SplashBurst 오브젝트를 연결합니다.")]
    [SerializeField]
    private GameObject splashObject;


    [Header("반복 시간")]

    [Tooltip("다음 물튀김이 발생하기까지의 최소 대기 시간입니다.")]
    [SerializeField]
    private float minimumInterval = 0.3f;

    [Tooltip("다음 물튀김이 발생하기까지의 최대 대기 시간입니다.")]
    [SerializeField]
    private float maximumInterval = 0.65f;

    [Tooltip("한 번의 물튀김이 화면에 보이는 시간입니다.")]
    [SerializeField]
    private float burstDuration = 0.22f;


    [Header("크기 변화")]

    [Tooltip("물튀김이 처음 나타날 때의 크기 배율입니다.")]
    [SerializeField]
    private float startingScaleMultiplier = 0.15f;

    [Tooltip("물튀김마다 적용할 최소 크기 배율입니다.")]
    [SerializeField]
    private float minimumScaleMultiplier = 0.85f;

    [Tooltip("물튀김마다 적용할 최대 크기 배율입니다.")]
    [SerializeField]
    private float maximumScaleMultiplier = 1.15f;


    [Header("시작 설정")]

    [Tooltip("게임 시작과 동시에 물튀김을 반복할지 결정합니다.")]
    [SerializeField]
    private bool playOnStart = true;


    // Inspector에서 설정한 SplashBurst의 원래 크기를 기억합니다.
    // 현재 설정한 0.05 크기가 기준 크기로 사용됩니다.
    private Vector3 originalLocalScale;

    // 현재 실행 중인 반복 코루틴을 기억합니다.
    // 나중에 누출을 멈출 때 안전하게 중지하기 위해 필요합니다.
    private Coroutine splashLoopCoroutine;

    // false가 되면 현재 보이는 스플래시는 끝까지 재생하고 다음 반복만 중지합니다.
    private bool leakRequested;


    private void Awake()
    {
        if (splashObject == null)
        {
            Debug.LogError(
                "[WaterSplashBurst] Splash Object가 연결되지 않았습니다.",
                this
            );

            return;
        }

        // 사용자가 Scene에서 맞춰놓은 현재 크기를 기준 크기로 저장합니다.
        originalLocalScale = splashObject.transform.localScale;

        // 게임 시작 전에는 물튀김 모델이 보이지 않도록 합니다.
        splashObject.SetActive(false);
    }


    private void Start()
    {
        if (playOnStart)
        {
            StartLeak();
        }
    }


    /// <summary>
    /// 물튀김 반복을 시작합니다.
    /// 나중에 플랜지 누출 전체 제어 스크립트에서도 호출할 수 있습니다.
    /// </summary>
    public void StartLeak()
    {
        if (splashObject == null)
        {
            return;
        }

        leakRequested = true;

        // 이미 실행 중이라면 중복으로 시작하지 않습니다.
        if (splashLoopCoroutine != null)
        {
            return;
        }

        splashLoopCoroutine = StartCoroutine(SplashLoop());
    }


    /// <summary>
    /// 현재 보이는 물튀김은 자연스럽게 끝내고 다음 반복을 멈춥니다.
    /// </summary>
    public void StopLeak()
    {
        leakRequested = false;
    }


    /// <summary>
    /// 임의의 간격으로 물튀김을 계속 재생합니다.
    /// </summary>
    private IEnumerator SplashLoop()
    {
        while (leakRequested)
        {
            yield return PlaySingleBurst();

            if (!leakRequested)
            {
                break;
            }

            float waitingTime = Random.Range(
                minimumInterval,
                maximumInterval
            );

            yield return new WaitForSeconds(waitingTime);
        }

        splashLoopCoroutine = null;
    }


    /// <summary>
    /// 물튀김 모델을 한 번 켜고,
    /// 빠르게 커졌다가 다시 작아지게 만든 후 숨깁니다.
    /// </summary>
    private IEnumerator PlaySingleBurst()
    {
        float randomScaleMultiplier = Random.Range(
            minimumScaleMultiplier,
            maximumScaleMultiplier
        );

        // 매번 같은 방향으로 보이지 않도록 Y축 회전을 조금씩 바꿉니다.
        float randomYRotation = Random.Range(0f, 360f);

        splashObject.transform.localRotation =
            Quaternion.Euler(0f, randomYRotation, 0f);

        splashObject.SetActive(true);

        float elapsedTime = 0f;

        while (elapsedTime < burstDuration)
        {
            elapsedTime += Time.deltaTime;

            float normalizedTime = Mathf.Clamp01(
                elapsedTime / burstDuration
            );

            /*
             * Sin 곡선을 사용합니다.
             *
             * 시작할 때는 작음
             * 중간에는 가장 크게 보임
             * 끝날 때 다시 작아짐
             *
             * 그래서 정적인 모델도 순간적으로 튀었다 사라지는 것처럼 보입니다.
             */
            float pulse = Mathf.Sin(normalizedTime * Mathf.PI);

            float currentScaleMultiplier = Mathf.Lerp(
                startingScaleMultiplier,
                randomScaleMultiplier,
                pulse
            );

            splashObject.transform.localScale =
                originalLocalScale * currentScaleMultiplier;

            yield return null;
        }

        splashObject.SetActive(false);
        splashObject.transform.localScale = originalLocalScale;
    }


    private void OnDisable()
    {
        leakRequested = false;

        // Scene 전환이나 오브젝트 비활성화 시 코루틴을 정리합니다.
        if (splashLoopCoroutine != null)
        {
            StopCoroutine(splashLoopCoroutine);
            splashLoopCoroutine = null;
        }

        if (splashObject != null)
        {
            splashObject.SetActive(false);
        }
    }
}
