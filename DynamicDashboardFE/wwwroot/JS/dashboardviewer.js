/* ================================================================== */
/* DASHBOARD VIEWER - fullscreen helpers                              */
/* Location: DynamicDashboardFE/wwwroot/JS/dashboardviewer.js         */
/* Called from DashboardViewerPage.razor (ToggleFullscreen).          */
/* Loaded once by index.html; replaces the copies that were defined   */
/* in the page's inline <script>. Vendor fallbacks cover older Safari */
/* (webkit) and legacy Edge (ms).                                     */
/* ================================================================== */

window.enterFullscreen = function () {
    const elem = document.documentElement;
    const request = elem.requestFullscreen || elem.webkitRequestFullscreen || elem.msRequestFullscreen;
    if (!request) {
        throw new Error('Fullscreen is not supported in this browser.');
    }

    const result = request.call(elem);
    if (result && typeof result.catch === 'function') {
        result.catch(err => console.warn('[DashboardViewer] Fullscreen request was refused:', err));
    }
};

window.exitFullscreen = function () {
    const exit = document.exitFullscreen || document.webkitExitFullscreen || document.msExitFullscreen;
    if (!exit || !(document.fullscreenElement || document.webkitFullscreenElement || document.msFullscreenElement)) {
        return;
    }

    const result = exit.call(document);
    if (result && typeof result.catch === 'function') {
        result.catch(err => console.warn('[DashboardViewer] Could not leave fullscreen:', err));
    }
};
