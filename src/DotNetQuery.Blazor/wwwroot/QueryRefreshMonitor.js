let _visibilityListener = null;
let _onlineListener = null;

export function register(dotnetRef, onFocus, onReconnect) {
    if (onFocus) {
        _visibilityListener = () => {
            if (!document.hidden) {
                dotnetRef.invokeMethodAsync('OnFocus');
            }
        };
        document.addEventListener('visibilitychange', _visibilityListener);
    }
    if (onReconnect) {
        _onlineListener = () => {
            dotnetRef.invokeMethodAsync('OnReconnect');
        };
        window.addEventListener('online', _onlineListener);
    }
}

/** Removes the listeners registered by {@link register}. Called from Blazor's DisposeAsync. */
export function unregister() {
    if (_visibilityListener) {
        document.removeEventListener('visibilitychange', _visibilityListener);
        _visibilityListener = null;
    }
    if (_onlineListener) {
        window.removeEventListener('online', _onlineListener);
        _onlineListener = null;
    }
}
