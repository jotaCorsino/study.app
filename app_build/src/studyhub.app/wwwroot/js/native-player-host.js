window.studyHubNativePlayerHost = {
    observe(element, dotNetReference) {
        if (!element || !dotNetReference) {
            return;
        }

        this.dispose(element);

        const state = {
            animationFrameId: 0,
            intervalId: 0,
            resizeObserver: null,
            onScroll: null,
            onResize: null
        };

        const notify = () => {
            const rect = element.getBoundingClientRect();
            const viewportWidth = Math.max(window.innerWidth || 0, 1);
            const viewportHeight = Math.max(window.innerHeight || 0, 1);
            const leftRatio = rect.left / viewportWidth;
            const topRatio = rect.top / viewportHeight;
            const widthRatio = rect.width / viewportWidth;
            const heightRatio = rect.height / viewportHeight;
            const isVisible =
                rect.width > 0 &&
                rect.height > 0 &&
                rect.bottom > 0 &&
                rect.right > 0 &&
                rect.top < viewportHeight &&
                rect.left < viewportWidth;

            dotNetReference.invokeMethodAsync(
                "HandleViewportChanged",
                leftRatio,
                topRatio,
                widthRatio,
                heightRatio,
                isVisible);
        };

        const scheduleNotify = () => {
            if (state.animationFrameId !== 0) {
                return;
            }

            state.animationFrameId = window.requestAnimationFrame(() => {
                state.animationFrameId = 0;
                notify();
            });
        };

        state.resizeObserver = new ResizeObserver(scheduleNotify);
        state.resizeObserver.observe(element);
        state.intervalId = window.setInterval(scheduleNotify, 250);
        state.onScroll = scheduleNotify;
        state.onResize = scheduleNotify;

        window.addEventListener("scroll", state.onScroll, true);
        window.addEventListener("resize", state.onResize);

        element.__studyHubNativePlayerHostState = state;
        scheduleNotify();
    },

    dispose(element) {
        const state = element?.__studyHubNativePlayerHostState;
        if (!state) {
            return;
        }

        if (state.animationFrameId) {
            window.cancelAnimationFrame(state.animationFrameId);
        }

        window.clearInterval(state.intervalId);
        window.removeEventListener("scroll", state.onScroll, true);
        window.removeEventListener("resize", state.onResize);
        state.resizeObserver?.disconnect();
        delete element.__studyHubNativePlayerHostState;
    }
};
