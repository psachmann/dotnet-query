// DotNetQuery DevTools — resize handle JS module

let _dotnetRef = null;

/** Called once from Blazor after the component mounts. */
export function init(dotnetRef) {
    _dotnetRef = dotnetRef;
}

/**
 * Begin a resize drag.
 * @param {HTMLElement} panelElement  The panel div whose height is being adjusted.
 * @param {number}      startY        Mouse clientY at the moment of mousedown.
 * @param {number}      startHeight   Panel height (px) at the moment of mousedown.
 */
export function startResize(panelElement, startY, startHeight) {
    let currentHeight = startHeight;

    function onMouseMove(e) {
        e.preventDefault();
        // Moving the mouse upward (negative delta) increases the panel height.
        const delta = startY - e.clientY;
        currentHeight = Math.max(150, startHeight + delta);
        panelElement.style.height = currentHeight + 'px';
    }

    function onMouseUp() {
        window.removeEventListener('mousemove', onMouseMove);
        window.removeEventListener('mouseup',   onMouseUp);

        if (_dotnetRef) {
            _dotnetRef.invokeMethodAsync('OnResizeEnd', currentHeight);
        }
    }

    window.addEventListener('mousemove', onMouseMove);
    window.addEventListener('mouseup',   onMouseUp);
}
