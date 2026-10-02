/* ================================================================== */
/* DASHBOARD CHARTS - SHARED CHART RENDERER                           */
/* Location: DynamicDashboardFE/wwwroot/JS/dashboard-charts.js        */
/* Loaded once by index.html (after the ApexCharts library).          */
/*                                                                    */
/* Used by the Dashboard Builder and the Dashboard Viewer, so both    */
/* pages draw charts the same way and no page overwrites the other's  */
/* global function (both used to define window.renderDynamicChart).   */
/*                                                                    */
/* Public API (window.DashboardCharts):                               */
/*   render(componentId, chartType, rows, title, renderOptions?)      */
/*       -> Promise<boolean>  true when the chart was drawn           */
/*   destroy(componentId, renderOptions?)                             */
/*   destroyAll()                                                     */
/*   buildOptions(chartType, rows, height)  -> ApexCharts options     */
/*   isAvailable()                          -> ApexCharts loaded?     */
/*   configure({ ... })                     -> override defaults      */
/*                                                                    */
/* Charts are drawn into <div id="chart-{componentId}"> by default.   */
/* renderOptions.containerId overrides the element id.                */
/* ================================================================== */

(function () {
    'use strict';

    // ==================== CONFIGURATION ====================
    // Defaults. Override at runtime with DashboardCharts.configure({ ... }).
    const config = {
        containerIdPrefix: 'chart-',   // <div id="chart-{componentId}">
        containerWaitMs: 2000,         // how long render() waits for its chart area to appear
        containerPollMs: 50,           // polling interval while waiting
        minHeight: 200,                // px; used when the card body is smaller than this
        maxSeries: 4,                  // max numeric columns drawn as separate series
        dataLabelsMaxPoints: 12,       // value labels on bars are hidden above this many points
        markersMaxPoints: 30,          // line/area point markers are hidden above this many points
        legendLabelMaxLength: 24,      // longer pie/donut legend entries are shortened with "…"
        axisLabelMaxHeight: 80,        // px; longer x-axis labels are trimmed by ApexCharts
        axisLabelMaxWidth: 160,        // px; longer y-axis labels (horizontal bars) are trimmed
        fontFamily: 'Inter, "Segoe UI", system-ui, sans-serif',
        colors: ['#667EEA', '#48BB78', '#F6AD55', '#9F7AEA', '#FC8181', '#14B8A6', '#8B5CF6', '#F472B6']
    };

    const LOG_PREFIX = '[DashboardCharts]';

    // containerId -> { chart, container } for every chart currently drawn
    const chartInstances = new Map();

    // Watches the page for chart areas that Blazor removes (e.g. a card switching to its
    // loading or error state) so their charts are released before ApexCharts tries to
    // redraw into an element that is no longer on the page.
    let detachObserver = null;

    // containerId -> render request number. A newer request for the same chart makes older,
    // still-waiting requests stop quietly, so overlapping calls can never wipe a newer chart.
    const renderVersions = new Map();

    // ==================== VALUE HELPERS ====================

    /** Converts a cell value to a finite number, or null when it is not numeric. */
    function toNumber(value) {
        if (value === null || value === undefined || value === '' || typeof value === 'boolean') {
            return null;
        }
        if (typeof value === 'number') {
            return Number.isFinite(value) ? value : null;
        }
        const parsed = Number(String(value).trim().replace(/,/g, ''));
        return Number.isFinite(parsed) ? parsed : null;
    }

    /** A column is numeric when every non-empty value is a number and at least one exists. */
    function isNumericColumn(rows, column) {
        let hasNumber = false;
        for (const row of rows) {
            const value = row[column];
            if (value === null || value === undefined || value === '') {
                continue;
            }
            if (toNumber(value) === null) {
                return false;
            }
            hasNumber = true;
        }
        return hasNumber;
    }

    /** Readable category text: blanks are labelled, SQL date-times are shortened. */
    function formatCategory(value) {
        if (value === null || value === undefined || value === '') {
            return '(blank)';
        }
        const text = String(value);
        const midnight = /^(\d{4}-\d{2}-\d{2})T00:00:00(\.0+)?(Z|[+-]\d{2}:\d{2})?$/.exec(text);
        if (midnight) {
            return midnight[1];
        }
        const dateTime = /^(\d{4}-\d{2}-\d{2})T(\d{2}:\d{2})/.exec(text);
        return dateTime ? `${dateTime[1]} ${dateTime[2]}` : text;
    }

    function formatNumber(value) {
        const number = toNumber(value);
        if (number === null) {
            return '';
        }
        return new Intl.NumberFormat(undefined, {
            maximumFractionDigits: 2,
            notation: Math.abs(number) >= 1000000 ? 'compact' : 'standard'
        }).format(number);
    }

    function shortenText(text, maxLength) {
        const value = String(text ?? '');
        return value.length > maxLength ? value.slice(0, maxLength - 1) + '…' : value;
    }

    // ==================== COLUMN ANALYSIS ====================

    /**
     * Chooses the label column (the first non-numeric column, otherwise the first column) and up to
     * maxSeries numeric value columns. This replaces the old fixed "column 1 = label,
     * column 2 = value" rule, which drew zeros when the query had a different column order.
     */
    function analyzeColumns(rows) {
        const columns = Object.keys(rows[0] || {});
        const numericColumns = columns.filter(column => isNumericColumn(rows, column));
        const labelColumn = columns.find(column => !numericColumns.includes(column)) ?? columns[0] ?? null;
        const valueColumns = numericColumns.filter(column => column !== labelColumn).slice(0, config.maxSeries);

        return {
            labelColumn,
            valueColumns,
            labelIsNumeric: numericColumns.includes(labelColumn)
        };
    }

    /** Maps the many ways a chart type can be written onto an ApexCharts type. */
    function normalizeChartType(chartType) {
        const value = String(chartType || 'bar').toLowerCase().replace(/[\s_-]/g, '');

        switch (value) {
            case 'pie':
                return { apexType: 'pie', circular: true, horizontal: false };
            case 'donut':
            case 'doughnut':
                return { apexType: 'donut', circular: true, horizontal: false };
            case 'line':
                return { apexType: 'line', circular: false, horizontal: false };
            case 'area':
                return { apexType: 'area', circular: false, horizontal: false };
            case 'horizontalbar':
            case 'barh':
            case 'hbar':
                return { apexType: 'bar', circular: false, horizontal: true };
            default:
                // 'bar', 'column' and anything unknown
                return { apexType: 'bar', circular: false, horizontal: false };
        }
    }

    // ==================== OPTIONS BUILDER ====================

    /**
     * Builds ApexCharts options for the given rows. Pure function (no DOM access).
     * Returns null when the rows contain nothing numeric to plot.
     */
    function buildOptions(chartType, rows, height) {
        if (!Array.isArray(rows) || rows.length === 0) {
            return null;
        }

        const analysis = analyzeColumns(rows);
        let valueColumns = analysis.valueColumns;
        let categories;

        if (valueColumns.length > 0) {
            categories = rows.map(row => formatCategory(row[analysis.labelColumn]));
        } else if (analysis.labelIsNumeric) {
            // A single numeric column: plot it against the row number.
            valueColumns = [analysis.labelColumn];
            categories = rows.map((_, index) => `#${index + 1}`);
        } else {
            return null;
        }

        const type = normalizeChartType(chartType);
        const pointCount = rows.length;

        const options = {
            chart: {
                type: type.apexType,
                height: height || '100%',
                fontFamily: config.fontFamily,
                toolbar: {
                    show: true,
                    tools: { download: true, selection: false, zoom: false, zoomin: false, zoomout: false, pan: false, reset: false }
                },
                animations: { enabled: true, easing: 'easeinout', speed: 500 },
                redrawOnParentResize: true,
                redrawOnWindowResize: true
            },
            colors: config.colors,
            tooltip: { y: { formatter: value => formatNumber(value) } }
        };

        // ---------- Pie / donut ----------
        if (type.circular) {
            const valueColumn = valueColumns[0];

            options.series = rows.map(row => toNumber(row[valueColumn]) ?? 0);
            options.labels = categories;
            options.legend = {
                show: true,
                position: 'bottom',
                fontSize: '13px',
                formatter: seriesName => shortenText(seriesName, config.legendLabelMaxLength)
            };
            options.dataLabels = {
                enabled: true,
                formatter: percent => `${Number(percent).toFixed(1)}%`
            };
            options.plotOptions = {
                pie: {
                    donut: {
                        size: '65%',
                        labels: {
                            show: type.apexType === 'donut',
                            value: { formatter: value => formatNumber(value) },
                            total: {
                                show: true,
                                label: 'Total',
                                formatter: w => formatNumber(w.globals.seriesTotals.reduce((sum, v) => sum + v, 0))
                            }
                        }
                    }
                }
            };
            return options;
        }

        // ---------- Bar / line / area ----------
        options.series = valueColumns.map(column => ({
            name: column,
            data: rows.map(row => toNumber(row[column]))
        }));

        options.xaxis = {
            categories: categories,
            labels: {
                trim: true,
                hideOverlappingLabels: true,
                maxHeight: config.axisLabelMaxHeight
            }
        };

        options.yaxis = {
            forceNiceScale: true,
            labels: { formatter: value => formatNumber(value) }
        };

        options.legend = { show: valueColumns.length > 1, position: 'top' };
        options.grid = { borderColor: '#eef2f7', strokeDashArray: 4 };

        if (type.apexType === 'bar') {
            options.plotOptions = {
                bar: {
                    horizontal: type.horizontal,
                    borderRadius: 6,
                    columnWidth: '60%',
                    barHeight: '65%',
                    dataLabels: { position: 'top' }
                }
            };
            options.dataLabels = {
                enabled: valueColumns.length === 1 && pointCount <= config.dataLabelsMaxPoints,
                formatter: value => formatNumber(value),
                offsetX: type.horizontal ? 24 : 0,
                offsetY: type.horizontal ? 0 : -18,
                style: { fontSize: '11px', colors: ['#304758'] }
            };

            if (type.horizontal) {
                // Horizontal bars: values run along the x-axis, categories along the y-axis.
                options.xaxis.labels.formatter = value => formatNumber(value);
                options.yaxis = { labels: { maxWidth: config.axisLabelMaxWidth } };
            }
        } else {
            options.stroke = { curve: 'smooth', width: 3 };
            options.markers = { size: pointCount > config.markersMaxPoints ? 0 : 4 };
            options.dataLabels = { enabled: false };
            options.fill = type.apexType === 'area'
                ? { type: 'gradient', gradient: { opacityFrom: 0.5, opacityTo: 0.08 } }
                : { type: 'solid' };
        }

        return options;
    }

    // ==================== DOM HELPERS ====================

    function resolveContainerId(componentId, renderOptions) {
        return (renderOptions && renderOptions.containerId) || `${config.containerIdPrefix}${componentId}`;
    }

    /** Waits until the element exists (Blazor may not have rendered it yet). Resolves null on timeout. */
    function waitForElementById(id, timeoutMs) {
        return new Promise(resolve => {
            const started = Date.now();
            const check = () => {
                const element = document.getElementById(id);
                if (element) {
                    resolve(element);
                } else if (Date.now() - started >= timeoutMs) {
                    resolve(null);
                } else {
                    setTimeout(check, config.containerPollMs);
                }
            };
            check();
        });
    }

    /**
     * Uses the full height of the card body when it is tall enough ('100%' is measured by
     * ApexCharts against the chart area's parent), otherwise falls back to minHeight.
     */
    function resolveHeight(container) {
        const parent = container.parentElement;
        if (!parent) {
            return config.minHeight;
        }
        const style = getComputedStyle(parent);
        const innerHeight = parent.clientHeight -
            (parseFloat(style.paddingTop) || 0) - (parseFloat(style.paddingBottom) || 0);
        return innerHeight >= config.minHeight ? '100%' : config.minHeight;
    }

    /** Shows a short message in place of a chart. */
    function showMessage(container, text) {
        const message = document.createElement('div');
        message.textContent = text;
        Object.assign(message.style, {
            height: '100%',
            minHeight: '120px',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            textAlign: 'center',
            padding: '12px',
            color: '#94a3b8',
            fontSize: '13px'
        });
        container.replaceChildren(message);
    }

    /** Destroys one tracked chart; errors from an already-removed element are ignored. */
    function destroyEntry(containerId, entry) {
        try {
            entry.chart.destroy();
        } catch (err) {
            // Its element was already removed by Blazor; nothing left to clean up.
        }
        chartInstances.delete(containerId);
    }

    /** Destroys the chart drawn in a container (without cancelling pending renders). */
    function disposeInstance(containerId) {
        const entry = chartInstances.get(containerId);
        if (entry) {
            destroyEntry(containerId, entry);
        }
        const container = document.getElementById(containerId);
        if (container) {
            container.replaceChildren();
        }
        stopWatchingWhenIdle();
    }

    /**
     * Releases every chart whose area is no longer on the page. Runs as a MutationObserver
     * callback, i.e. right after Blazor's DOM update and before the browser's next layout,
     * which is when ApexCharts would otherwise react to the size change and throw.
     */
    function releaseDetachedCharts() {
        chartInstances.forEach((entry, containerId) => {
            if (!entry.container.isConnected) {
                destroyEntry(containerId, entry);
            }
        });
        stopWatchingWhenIdle();
    }

    function startWatchingForRemovedCharts() {
        if (detachObserver || typeof MutationObserver !== 'function' || !document.body) {
            return;
        }
        detachObserver = new MutationObserver(releaseDetachedCharts);
        detachObserver.observe(document.body, { childList: true, subtree: true });
    }

    function stopWatchingWhenIdle() {
        if (detachObserver && chartInstances.size === 0) {
            detachObserver.disconnect();
            detachObserver = null;
        }
    }

    // ==================== PUBLIC API ====================

    /**
     * Draws (or redraws) the chart for a component.
     * Waits up to containerWaitMs for <div id="chart-{componentId}"> to appear, so it works even
     * when called right after Blazor is asked to render. Returns true when the chart was drawn.
     */
    async function render(componentId, chartType, rows, title, renderOptions) {
        const containerId = resolveContainerId(componentId, renderOptions);
        const version = (renderVersions.get(containerId) || 0) + 1;
        renderVersions.set(containerId, version);
        const isOutdated = () => renderVersions.get(containerId) !== version;

        if (!isAvailable()) {
            console.error(`${LOG_PREFIX} ApexCharts is not loaded; cannot draw '${containerId}'.`);
            return false;
        }
        if (!Array.isArray(rows) || rows.length === 0) {
            console.warn(`${LOG_PREFIX} '${containerId}': no rows to draw.`);
            return false;
        }

        const container = await waitForElementById(containerId, config.containerWaitMs);
        if (isOutdated()) {
            return false;
        }
        if (!container) {
            console.warn(`${LOG_PREFIX} '${containerId}': chart area not on the page after ${config.containerWaitMs} ms.`);
            return false;
        }

        disposeInstance(containerId);

        if (title) {
            container.setAttribute('role', 'img');
            container.setAttribute('aria-label', String(title));
        }

        const options = buildOptions(chartType, rows, resolveHeight(container));
        if (!options) {
            showMessage(container, 'This query has no numeric column to chart.');
            return false;
        }

        try {
            const chart = new window.ApexCharts(container, options);
            chartInstances.set(containerId, { chart, container });
            startWatchingForRemovedCharts();
            await chart.render();
            return !isOutdated();
        } catch (err) {
            if (isOutdated()) {
                return false;
            }
            chartInstances.delete(containerId);
            stopWatchingWhenIdle();
            console.error(`${LOG_PREFIX} '${containerId}': drawing failed.`, err);
            showMessage(container, 'The chart could not be drawn. Details are in the browser console.');
            return false;
        }
    }

    /** Removes a component's chart and cancels any of its pending renders. */
    function destroy(componentId, renderOptions) {
        const containerId = resolveContainerId(componentId, renderOptions);
        renderVersions.set(containerId, (renderVersions.get(containerId) || 0) + 1);
        disposeInstance(containerId);
    }

    /** Removes every chart this module has drawn (used when a page is disposed or rebuilt). */
    function destroyAll() {
        Array.from(chartInstances.keys()).forEach(containerId => {
            renderVersions.set(containerId, (renderVersions.get(containerId) || 0) + 1);
            disposeInstance(containerId);
        });
    }

    function isAvailable() {
        return typeof window.ApexCharts === 'function';
    }

    /** Overrides defaults, e.g. DashboardCharts.configure({ minHeight: 240 }). */
    function configure(options) {
        if (!options || typeof options !== 'object') {
            return;
        }
        Object.keys(options).forEach(key => {
            if (Object.prototype.hasOwnProperty.call(config, key)) {
                config[key] = options[key];
            }
        });
    }

    window.DashboardCharts = {
        render,
        destroy,
        destroyAll,
        buildOptions,
        isAvailable,
        configure
    };
})();
