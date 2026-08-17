using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JH.EntrySafety
{
    /// <summary>
    /// Mode button frame: white by default, orange only while the pointer/ray is hovering.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TitleMenuFrameHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        Image _frame;
        Color _idle = Color.white;
        Color _hover = Color.white;

        public void Configure(Image frame, Color idle, Color hover)
        {
            _frame = frame;
            _idle = idle;
            _hover = hover;
            ApplyIdle();
        }

        public void ApplyIdle()
        {
            if (_frame != null)
                _frame.color = _idle;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_frame != null)
                _frame.color = _hover;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            ApplyIdle();
        }
    }
}
