// DotNetQuery DevTools JS module

const THEME_KEY = 'dnq-devtools-theme';

let _dotnetRef = null;

/** Called once from Blazor after the component mounts. */
export function init(dotnetRef) {
    _dotnetRef = dotnetRef;
}

/** Returns the persisted theme ('dark' or 'light'), defaulting to 'dark'. */
export function getTheme() {
    return localStorage.getItem(THEME_KEY) ?? 'dark';
}

/** Persists the chosen theme. */
export function setTheme(theme) {
    localStorage.setItem(THEME_KEY, theme);
}

/**
 * Begin a vertical resize drag (panel height).
 * @param {HTMLElement} panelElement  The panel div whose height is being adjusted.
 * @param {number}      startY        Mouse clientY at the moment of mousedown.
 * @param {number}      startHeight   Panel height (px) at the moment of mousedown.
 */
export function startResize(panelElement, startY, startHeight) {
    let currentHeight = startHeight;

    document.body.style.cursor     = 'ns-resize';
    document.body.style.userSelect = 'none';

    function onMouseMove(e) {
        // Moving the mouse upward (negative delta) increases the panel height.
        const delta = startY - e.clientY;
        currentHeight = Math.max(150, startHeight + delta);
        panelElement.style.height = currentHeight + 'px';
    }

    function onMouseUp() {
        document.body.style.cursor     = '';
        document.body.style.userSelect = '';
        window.removeEventListener('mousemove', onMouseMove);
        window.removeEventListener('mouseup',   onMouseUp);

        if (_dotnetRef) {
            _dotnetRef.invokeMethodAsync('OnResizeEnd', currentHeight);
        }
    }

    window.addEventListener('mousemove', onMouseMove);
    window.addEventListener('mouseup',   onMouseUp);
}

/**
 * Begin a horizontal resize drag (detail panel width).
 * @param {HTMLElement} detailElement  The detail div whose width is being adjusted.
 * @param {number}      startX         Mouse clientX at the moment of mousedown.
 * @param {number}      startWidth     Detail width (px) at the moment of mousedown.
 */
export function startDetailResize(detailElement, startX, startWidth) {
    let currentWidth = startWidth;

    document.body.style.cursor     = 'ew-resize';
    document.body.style.userSelect = 'none';

    function onMouseMove(e) {
        // Moving the mouse leftward (negative delta) increases the detail width.
        const delta = startX - e.clientX;
        currentWidth = Math.max(200, startWidth + delta);
        detailElement.style.width = currentWidth + 'px';
    }

    function onMouseUp() {
        document.body.style.cursor     = '';
        document.body.style.userSelect = '';
        window.removeEventListener('mousemove', onMouseMove);
        window.removeEventListener('mouseup',   onMouseUp);

        if (_dotnetRef) {
            _dotnetRef.invokeMethodAsync('OnDetailResizeEnd', currentWidth);
        }
    }

    window.addEventListener('mousemove', onMouseMove);
    window.addEventListener('mouseup',   onMouseUp);
}
