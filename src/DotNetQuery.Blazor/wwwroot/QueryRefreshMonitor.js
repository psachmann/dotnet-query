/**
 * Registers the focus/reconnect listeners for one <QueryRefreshMonitor> and returns a handle that owns
 * them. The listeners live in the handle rather than in module state: the module is shared by every
 * monitor on the page, so module-level references would be overwritten by a second instance and each
 * one's dispose would then remove the wrong listeners.
 */
export function register(dotnetRef, onFocus, onReconnect) {
    let visibilityListener = null;
    let onlineListener = null;

    if (onFocus) {
        visibilityListener = () => {
            if (!document.hidden) {
                dotnetRef.invokeMethodAsync('OnFocus');
            }
        };
        document.addEventListener('visibilitychange', visibilityListener);
    }
    if (onReconnect) {
        onlineListener = () => {
            dotnetRef.invokeMethodAsync('OnReconnect');
        };
        window.addEventListener('online', onlineListener);
    }

    return {
        /** Removes the listeners this handle registered. Called from Blazor's DisposeAsync. */
        unregister() {
            if (visibilityListener) {
                document.removeEventListener('visibilitychange', visibilityListener);
                visibilityListener = null;
            }
            if (onlineListener) {
                window.removeEventListener('online', onlineListener);
                onlineListener = null;
            }
        },
    };
}
