/* ================================================================== */
/* DASHBOARD BUILDER - JAVASCRIPT                                     */
/* Location: DynamicDashboardFE/wwwroot/JS/dashboard-builder.js       */
/* Loaded once by index.html.                                         */
/*                                                                    */
/* A) Functions the Dashboard Builder page calls from C#.             */
/*    The global names are unchanged so existing calls keep working:  */
/*      registerKeyboardHandler(dotNetRef)                            */
/*      unregisterKeyboardHandler()                                   */
/*      getElementBounds(element)                                     */
/*      updateDropIndicator(canPlace)                                 */
/*      addResizeListeners(dotNetRef, direction?)                     */
/*      removeResizeListeners()                                       */
/*                                                                    */
/* B) window.DashboardBuilder: the original helper API (drag ghost,   */
/*    drop zone, sortable items, resize, utilities), repaired.        */
/*    The previous version of this file did not parse, so none of it  */
/*    had ever run. Its helpers now act only on drags they start      */
/*    themselves, so they never interfere with Blazor's drag & drop.  */
/*                                                                    */
/* Chart drawing is NOT in this file: it lives in the shared          */
/* dashboard-charts.js, used by both the builder and the viewer.      */
/* ================================================================== */

(function () {
    'use strict';

    // ==================== CONFIGURATION ====================
    // Defaults. Override at runtime with DashboardBuilder.configure({ ... }).
    const config = {
        gridColumns: 12,     // matches grid-template-columns: repeat(12, 1fr)
        gridRowHeight: 100,  // matches grid-template-rows: repeat(n, 100px)
        gridGap: 16          // matches .dashboard-grid { gap: 16px }
    };

    const LOG_PREFIX = '[DashboardBuilder]';

    const RESIZE_CURSORS = {
        n: 'ns-resize', s: 'ns-resize',
        e: 'ew-resize', w: 'ew-resize',
        ne: 'nesw-resize', sw: 'nesw-resize',
        nw: 'nwse-resize', se: 'nwse-resize'
    };

    // ==================== SHARED HELPERS ====================

    /** Calls a [JSInvokable] .NET method; logs instead of throwing if the call fails. */
    function invokeDotNet(dotNetRef, methodName, ...args) {
        if (!dotNetRef || typeof dotNetRef.invokeMethodAsync !== 'function') {
            return Promise.resolve();
        }
        return dotNetRef.invokeMethodAsync(methodName, ...args).catch(err => {
            console.error(`${LOG_PREFIX} .NET call '${methodName}' failed:`, err);
        });
    }

    /** True while the user is typing in a form field (shortcuts must not fire). */
    function isTypingTarget(target) {
        if (!target) {
            return false;
        }
        const tag = target.tagName;
        return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || target.isContentEditable === true;
    }

    function isBuilderOnPage() {
        return document.querySelector('.dashboard-builder') !== null;
    }

    function isModalOpen() {
        return document.querySelector('.modal.show') !== null;
    }

    function hasSelectedComponent() {
        return document.querySelector('.dashboard-builder .dashboard-component.selected') !== null;
    }

    /** True when focus is on the page itself or the canvas (not on a button, link, etc.). */
    function isCanvasFocus(target) {
        return !target || target === document.body || target === document.documentElement ||
            (typeof target.closest === 'function' && target.closest('.canvas-drop-zone') !== null);
    }

    /** Converts a DOM mouse event into the shape of Blazor's MouseEventArgs. */
    function toMouseEventArgs(e) {
        return {
            type: e.type,
            detail: e.detail,
            screenX: e.screenX,
            screenY: e.screenY,
            clientX: e.clientX,
            clientY: e.clientY,
            offsetX: e.offsetX,
            offsetY: e.offsetY,
            pageX: e.pageX,
            pageY: e.pageY,
            movementX: e.movementX,
            movementY: e.movementY,
            button: e.button,
            buttons: e.buttons,
            ctrlKey: e.ctrlKey,
            shiftKey: e.shiftKey,
            altKey: e.altKey,
            metaKey: e.metaKey
        };
    }

    /** Column and row pitch (cell size including the gap) for a canvas element. */
    function getGridPitch(canvas) {
        const width = canvas.getBoundingClientRect().width;
        return {
            colPitch: (width + config.gridGap) / config.gridColumns,
            rowPitch: config.gridRowHeight + config.gridGap
        };
    }

    // ==================== A) KEYBOARD SHORTCUTS ====================
    let keyboardHandler = null;

    /**
     * Registers the builder's keyboard shortcuts. Any previous registration is removed first,
     * so revisiting the page never stacks duplicate handlers.
     *
     * Shortcuts act only while the builder is on screen, no dialog is open and the user is not
     * typing. Shortcuts that act on a component need a selected component; otherwise the keys keep
     * their normal browser behavior (arrow keys scroll, Tab moves focus between controls).
     */
    window.registerKeyboardHandler = function (dotNetRef) {
        window.unregisterKeyboardHandler();

        keyboardHandler = function (e) {
            if (e.isComposing || !isBuilderOnPage() || isModalOpen() || isTypingTarget(e.target)) {
                return;
            }

            const key = e.key;
            const lowerKey = typeof key === 'string' && key.length === 1 ? key.toLowerCase() : key;
            const ctrlOrMeta = e.ctrlKey || e.metaKey;
            const selected = hasSelectedComponent();

            // Ctrl/Cmd + S: save (with or without a selection)
            if (ctrlOrMeta && lowerKey === 's') {
                e.preventDefault();
                invokeDotNet(dotNetRef, 'OnSaveKey');
                return;
            }

            // Tab: cycle components. Without a selection it starts cycling only when focus is on the
            // page or canvas, so Tab still moves between buttons and links normally.
            if (key === 'Tab' && (selected || isCanvasFocus(e.target))) {
                e.preventDefault();
                invokeDotNet(dotNetRef, 'OnTabKey', e.shiftKey);
                return;
            }

            if (!selected) {
                return;
            }

            // Arrow keys: move (Shift = resize, Ctrl = bigger step)
            if (key === 'ArrowUp' || key === 'ArrowDown' || key === 'ArrowLeft' || key === 'ArrowRight') {
                e.preventDefault();
                invokeDotNet(dotNetRef, 'OnArrowKey', key, e.shiftKey, e.ctrlKey);
                return;
            }

            // Escape: deselect
            if (key === 'Escape') {
                invokeDotNet(dotNetRef, 'OnEscapeKey');
                return;
            }

            // Delete removes the selected component. Backspace needs Ctrl/Cmd, so an accidental
            // Backspace never deletes a component.
            if (key === 'Delete' || (key === 'Backspace' && ctrlOrMeta)) {
                e.preventDefault();
                invokeDotNet(dotNetRef, 'OnDeleteKey');
                return;
            }

            // Ctrl/Cmd + D: duplicate
            if (ctrlOrMeta && lowerKey === 'd') {
                e.preventDefault();
                invokeDotNet(dotNetRef, 'OnDuplicateKey');
            }
        };

        document.addEventListener('keydown', keyboardHandler);
        console.log(`${LOG_PREFIX} keyboard handler registered`);
    };

    /** Removes the keyboard shortcuts (called when the builder page is disposed). */
    window.unregisterKeyboardHandler = function () {
        if (keyboardHandler) {
            document.removeEventListener('keydown', keyboardHandler);
            keyboardHandler = null;
        }
    };

    // ==================== A) ELEMENT BOUNDS ====================

    /** Returns the element's position, size and scroll offsets (used for grid-position maths). */
    window.getElementBounds = function (element) {
        if (!element || typeof element.getBoundingClientRect !== 'function') {
            return null;
        }
        const rect = element.getBoundingClientRect();
        return {
            left: rect.left,
            top: rect.top,
            width: rect.width,
            height: rect.height,
            scrollLeft: element.scrollLeft || 0,
            scrollTop: element.scrollTop || 0
        };
    };

    // ==================== A) DROP INDICATOR STATE ====================

    /**
     * Colors the Blazor-rendered drop indicator green (can drop) or red (position occupied).
     * Only CSS classes are changed; Blazor keeps ownership of the element and its content.
     * If the indicator is not on the page yet this does nothing; the next drag-over updates it.
     */
    window.updateDropIndicator = function (canPlace) {
        const indicator = document.querySelector('.dashboard-builder .drop-indicator');
        if (!indicator) {
            return;
        }

        const allowed = canPlace !== false;
        const inner = indicator.querySelector('.drop-indicator-inner');

        [indicator, inner].forEach(element => {
            if (element) {
                element.classList.toggle('can-drop', allowed);
                element.classList.toggle('cannot-drop', !allowed);
            }
        });
    };

    // ==================== A) MOUSE RESIZE ====================
    let resizeSession = null;

    /**
     * Starts a mouse-resize session: reports pointer moves to OnResizeMove (at most once per
     * animation frame) and calls OnResizeEnd on mouse-up, Escape, or when the window loses focus.
     * Text selection is disabled while resizing. The optional direction ('n', 'se', ...) sets the cursor.
     */
    window.addResizeListeners = function (dotNetRef, direction) {
        window.removeResizeListeners();

        let pendingEvent = null;
        let frameRequested = false;
        const previousUserSelect = document.body.style.userSelect;
        const previousCursor = document.body.style.cursor;

        const onMouseMove = e => {
            pendingEvent = e;
            if (frameRequested) {
                return;
            }
            frameRequested = true;
            requestAnimationFrame(() => {
                frameRequested = false;
                if (resizeSession && pendingEvent) {
                    invokeDotNet(dotNetRef, 'OnResizeMove', toMouseEventArgs(pendingEvent));
                    pendingEvent = null;
                }
            });
        };

        const finish = notifyDotNet => {
            document.removeEventListener('mousemove', onMouseMove);
            document.removeEventListener('mouseup', onMouseUp);
            document.removeEventListener('keydown', onKeyDown);
            window.removeEventListener('blur', onWindowBlur);
            document.body.style.userSelect = previousUserSelect;
            document.body.style.cursor = previousCursor;
            resizeSession = null;
            if (notifyDotNet) {
                invokeDotNet(dotNetRef, 'OnResizeEnd');
            }
        };

        const onMouseUp = () => finish(true);
        const onKeyDown = e => {
            if (e.key === 'Escape') {
                finish(true);
            }
        };
        const onWindowBlur = () => finish(true);

        document.addEventListener('mousemove', onMouseMove);
        document.addEventListener('mouseup', onMouseUp);
        document.addEventListener('keydown', onKeyDown);
        window.addEventListener('blur', onWindowBlur);

        document.body.style.userSelect = 'none';
        if (direction && RESIZE_CURSORS[direction]) {
            document.body.style.cursor = RESIZE_CURSORS[direction];
        }

        resizeSession = { finish };
    };

    /** Ends any active resize session without notifying .NET (used before starting a new one). */
    window.removeResizeListeners = function () {
        if (resizeSession) {
            resizeSession.finish(false);
        }
    };

    // ==================== B) ORIGINAL HELPER API (REPAIRED) ====================
    const DashboardBuilder = {
        // ---------- State ----------
        draggedElement: null,
        draggedData: null,
        dropZones: [],
        gridSize: { cols: config.gridColumns, rowHeight: config.gridRowHeight },
        initialized: false,

        // ---------- Configuration ----------
        /** Overrides defaults, e.g. DashboardBuilder.configure({ gridRowHeight: 120 }). */
        configure: function (options) {
            if (!options || typeof options !== 'object') {
                return;
            }
            Object.keys(options).forEach(key => {
                if (Object.prototype.hasOwnProperty.call(config, key)) {
                    config[key] = options[key];
                }
            });
            this.gridSize.cols = config.gridColumns;
            this.gridSize.rowHeight = config.gridRowHeight;
        },

        // ---------- Initialization ----------
        init: function () {
            if (this.initialized) {
                return;
            }
            this.initialized = true;
            this.setupGlobalListeners();
            console.log(`${LOG_PREFIX} initialized`);
        },

        setupGlobalListeners: function () {
            // Allow dropping while one of this helper's own drags is active.
            document.addEventListener('dragover', e => {
                if (DashboardBuilder.draggedData) {
                    e.preventDefault();
                }
            });

            // Inside the builder, stop the browser from opening a dropped file or link.
            // Other pages (file-upload drop areas) keep their normal behavior.
            document.addEventListener('drop', e => {
                const target = e.target;
                const isFileInput = target && target.tagName === 'INPUT' && target.type === 'file';
                const insideBuilder = target && typeof target.closest === 'function' &&
                    target.closest('.dashboard-builder') !== null;
                if (!isFileInput && (insideBuilder || DashboardBuilder.draggedData)) {
                    e.preventDefault();
                }
            });

            // Escape cancels one of this helper's own drags.
            document.addEventListener('keydown', e => {
                if (e.key === 'Escape' && DashboardBuilder.draggedData) {
                    DashboardBuilder.cancelDrag();
                }
            });
        },

        // ---------- Drag start ----------
        startDrag: function (element, componentData) {
            this.draggedElement = element || null;
            this.draggedData = componentData || {};

            if (element && element.classList) {
                element.classList.add('is-dragging');
            }
            document.body.classList.add('is-dragging-active');

            this.createDragGhost(element, this.draggedData);

            console.log(`${LOG_PREFIX} started dragging:`, this.draggedData.name || '(component)');
            return true;
        },

        startDragExisting: function (element, componentId, gridWidth, gridHeight) {
            this.draggedElement = element || null;
            this.draggedData = {
                type: 'existing',
                componentId: componentId,
                gridWidth: gridWidth,
                gridHeight: gridHeight
            };

            if (element && element.classList) {
                element.classList.add('is-dragging');
            }
            document.body.classList.add('is-dragging-active');

            console.log(`${LOG_PREFIX} started dragging existing component:`, componentId);
            return true;
        },

        // ---------- Drag ghost (follows the pointer) ----------
        createDragGhost: function (element, data) {
            this.removeDragGhost();

            const ghost = document.createElement('div');
            ghost.id = 'drag-ghost';
            ghost.className = 'db-drag-ghost';
            Object.assign(ghost.style, {
                position: 'fixed',
                left: '-9999px',
                top: '-9999px',
                zIndex: '2000',
                pointerEvents: 'none',
                display: 'flex',
                alignItems: 'center',
                gap: '8px',
                padding: '8px 12px',
                borderRadius: '10px',
                background: '#ffffff',
                boxShadow: '0 8px 24px rgba(15, 23, 42, 0.18)',
                font: '600 13px Inter, system-ui, sans-serif',
                color: '#1e293b'
            });

            const icon = document.createElement('div');
            icon.className = 'db-ghost-icon';
            Object.assign(icon.style, {
                width: '28px',
                height: '28px',
                borderRadius: '8px',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                color: '#ffffff',
                background: '#3B82F6'
            });
            if (data && data.color) {
                icon.style.background = data.color;
            }

            const glyph = document.createElement('i');
            glyph.className = (data && data.icon) || 'fas fa-cube';
            icon.appendChild(glyph);

            // textContent (not innerHTML) so a component name can never inject markup
            const label = document.createElement('span');
            label.textContent = (data && data.name) || 'Component';

            ghost.append(icon, label);
            document.body.appendChild(ghost);

            document.addEventListener('mousemove', this.moveDragGhost);
            document.addEventListener('dragover', this.moveDragGhost);
        },

        // Used as an event listener, so it must not rely on "this".
        moveDragGhost: function (e) {
            const ghost = document.getElementById('drag-ghost');
            if (ghost) {
                ghost.style.left = (e.clientX + 15) + 'px';
                ghost.style.top = (e.clientY + 15) + 'px';
            }
        },

        removeDragGhost: function () {
            const ghost = document.getElementById('drag-ghost');
            if (ghost) {
                ghost.remove();
            }
            document.removeEventListener('mousemove', this.moveDragGhost);
            document.removeEventListener('dragover', this.moveDragGhost);
        },

        // ---------- Drag end ----------
        endDrag: function () {
            if (this.draggedElement && this.draggedElement.classList) {
                this.draggedElement.classList.remove('is-dragging');
            }
            document.body.classList.remove('is-dragging-active');
            this.removeDragGhost();
            this.removeDropIndicator();

            this.draggedElement = null;
            this.draggedData = null;

            console.log(`${LOG_PREFIX} drag ended`);
        },

        cancelDrag: function () {
            this.endDrag();
            console.log(`${LOG_PREFIX} drag cancelled`);
        },

        // ---------- Drop zone (reacts only to this helper's own drags) ----------
        setupDropZone: function (canvasElement) {
            if (!canvasElement) {
                return;
            }

            // Forget zones Blazor has removed, and never wire the same element twice.
            this.dropZones = this.dropZones.filter(zone => zone.isConnected);
            if (this.dropZones.includes(canvasElement)) {
                return;
            }
            this.dropZones.push(canvasElement);

            canvasElement.addEventListener('dragenter', e => {
                if (!DashboardBuilder.draggedData) {
                    return;
                }
                e.preventDefault();
                canvasElement.classList.add('drag-over');
            });

            canvasElement.addEventListener('dragleave', e => {
                if (!canvasElement.contains(e.relatedTarget)) {
                    canvasElement.classList.remove('drag-over');
                }
            });

            canvasElement.addEventListener('dragover', e => {
                if (!DashboardBuilder.draggedData) {
                    return;
                }
                e.preventDefault();
                DashboardBuilder.updateDropIndicator(canvasElement, e);
            });

            canvasElement.addEventListener('drop', () => {
                canvasElement.classList.remove('drag-over');
                DashboardBuilder.removeDropIndicator();
            });

            console.log(`${LOG_PREFIX} drop zone configured`);
        },

        // ---------- Drop indicator for this helper's own drags ----------
        // (Blazor's own drags use the Blazor-rendered indicator plus window.updateDropIndicator.)
        updateDropIndicator: function (canvas, event) {
            const position = this.calculateDropPosition(canvas, event);
            const width = this.draggedData?.gridWidth || this.draggedData?.defaultWidth || 4;
            const height = this.draggedData?.gridHeight || this.draggedData?.defaultHeight || 2;

            this.showDropIndicator(canvas, position.gridX, position.gridY, width, height);
            return position;
        },

        showDropIndicator: function (canvas, gridX, gridY, width, height) {
            if (!canvas) {
                return;
            }

            let indicator = document.getElementById('drop-indicator');
            if (!indicator) {
                indicator = document.createElement('div');
                indicator.id = 'drop-indicator';
                indicator.className = 'db-drop-indicator-js';
                Object.assign(indicator.style, {
                    position: 'absolute',
                    zIndex: '1000',
                    pointerEvents: 'none',
                    display: 'none',
                    alignItems: 'center',
                    justifyContent: 'center',
                    border: '3px dashed #22c55e',
                    borderRadius: '12px',
                    background: 'rgba(34, 197, 94, 0.18)',
                    color: '#16a34a',
                    fontSize: '1.5rem'
                });

                const glyph = document.createElement('i');
                glyph.className = 'fas fa-plus';
                indicator.appendChild(glyph);

                if (getComputedStyle(canvas).position === 'static') {
                    canvas.style.position = 'relative';
                }
                canvas.appendChild(indicator);
            }

            const pitch = getGridPitch(canvas);
            indicator.style.left = (gridX * pitch.colPitch) + 'px';
            indicator.style.top = (gridY * pitch.rowPitch) + 'px';
            indicator.style.width = (width * pitch.colPitch - config.gridGap) + 'px';
            indicator.style.height = (height * pitch.rowPitch - config.gridGap) + 'px';
            indicator.style.display = 'flex';
        },

        removeDropIndicator: function () {
            const indicator = document.getElementById('drop-indicator');
            if (indicator) {
                indicator.style.display = 'none';
            }
        },

        // ---------- Drop position (grid cell under the pointer) ----------
        calculateDropPosition: function (canvasElement, event) {
            if (!canvasElement || !event) {
                return { gridX: 0, gridY: 0 };
            }

            const rect = canvasElement.getBoundingClientRect();
            const pitch = getGridPitch(canvasElement);
            const x = event.clientX - rect.left + (canvasElement.scrollLeft || 0);
            const y = event.clientY - rect.top + (canvasElement.scrollTop || 0);

            const width = this.draggedData?.gridWidth || this.draggedData?.defaultWidth || 4;
            const gridX = Math.floor(x / pitch.colPitch);
            const gridY = Math.floor(y / pitch.rowPitch);

            return {
                gridX: Math.max(0, Math.min(gridX, config.gridColumns - width)),
                gridY: Math.max(0, gridY)
            };
        },

        // ---------- Grab cursor on item headers ----------
        makeGridItemsSortable: function (containerSelector) {
            const container = document.querySelector(containerSelector);
            if (!container) {
                return;
            }

            container.querySelectorAll('.db-grid-item, .dashboard-component').forEach(item => {
                const header = item.querySelector('.db-item-header, .component-header');
                if (!header || header.dataset.dbSortable === '1') {
                    return;
                }
                header.dataset.dbSortable = '1';
                header.style.cursor = 'grab';
                header.addEventListener('mousedown', () => { header.style.cursor = 'grabbing'; });
                header.addEventListener('mouseup', () => { header.style.cursor = 'grab'; });
            });
        },

        // ---------- Resize ----------
        // Uses the same mechanism as the builder page's own resize handles (OnResizeMove /
        // OnResizeEnd), so there is a single resize implementation. The original version called
        // a .NET method 'OnComponentResized' that does not exist on the page.
        initResize: function (element, componentId, direction, dotNetHelper) {
            window.addResizeListeners(dotNetHelper, direction);
        },

        // ---------- Utilities ----------
        getDraggedData: function () {
            return this.draggedData;
        },

        setGridSize: function (cols, rowHeight) {
            this.configure({ gridColumns: cols, gridRowHeight: rowHeight });
        },

        focusElement: function (element) {
            if (!element || typeof element.focus !== 'function') {
                return;
            }
            element.focus();
            if (typeof element.select === 'function') {
                element.select();
            }
        },

        copyToClipboard: async function (text) {
            try {
                await navigator.clipboard.writeText(text);
                return true;
            } catch (err) {
                console.error(`${LOG_PREFIX} failed to copy:`, err);
                return false;
            }
        },

        downloadJson: function (data, filename) {
            const json = typeof data === 'string' ? data : JSON.stringify(data, null, 2);
            const blob = new Blob([json], { type: 'application/json' });
            const url = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url;
            link.download = filename || 'dashboard.json';
            document.body.appendChild(link);
            link.click();
            document.body.removeChild(link);
            URL.revokeObjectURL(url);
        },

        // Same shortcuts as registerKeyboardHandler (single implementation). The original version
        // called .NET methods (OnKeyboardSave, OnKeyboardUndo, ...) that do not exist on the page.
        setupKeyboardShortcuts: function (dotNetHelper) {
            window.registerKeyboardHandler(dotNetHelper);
        }
    };

    window.DashboardBuilder = DashboardBuilder;

    // ==================== AUTO-INITIALIZE ====================
    // init() is idempotent, so running it once on load is enough.
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => DashboardBuilder.init());
    } else {
        DashboardBuilder.init();
    }
})();
