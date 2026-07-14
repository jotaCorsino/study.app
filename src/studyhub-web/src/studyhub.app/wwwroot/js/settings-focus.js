window.studyHubSettingsFocus = {
    focusById(elementId) {
        window.requestAnimationFrame(() => {
            document.getElementById(elementId)?.focus({ preventScroll: true });
        });
    }
};
