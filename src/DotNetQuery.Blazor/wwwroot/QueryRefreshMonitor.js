export function register(dotnetRef, onFocus, onReconnect) {
    if (onFocus) {
        document.addEventListener('visibilitychange', () => {
            if (!document.hidden) {
                dotnetRef.invokeMethodAsync('OnFocus');
            }
        });
    }
    if (onReconnect) {
        window.addEventListener('online', () => {
            dotnetRef.invokeMethodAsync('OnReconnect')
        });
    }
}
