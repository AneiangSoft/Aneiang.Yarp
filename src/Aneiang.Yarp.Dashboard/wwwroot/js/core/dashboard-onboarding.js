/**
 * Onboarding Wizard
 * Detects first-time setup (no clusters/routes) and guides user through 3 steps.
 * State persisted in localStorage: 'onboarding_completed' = '1' when done or skipped.
 */
(function() {
    'use strict';

    function t(key, fallback) {
        return (window.__ && window.__(key)) || fallback;
    }

    function getPrefix() {
        return (window.dashboardConfig && window.dashboardConfig.prefix) || 'apigateway';
    }

    async function checkNeedsOnboarding() {
        try {
            var completed = localStorage.getItem('onboarding_completed');
            if (completed === '1') return false;

            var results = await Promise.all([
                window.DashboardApi ? DashboardApi.endpoints.getClusters().catch(function() { return []; }) : Promise.resolve([]),
                window.DashboardApi ? DashboardApi.endpoints.getRoutes().catch(function() { return []; }) : Promise.resolve([])
            ]);

            var clusters = Array.isArray(results[0]) ? results[0] : (results[0] && results[0].data) || [];
            var routes = Array.isArray(results[1]) ? results[1] : (results[1] && results[1].data) || [];

            return clusters.length === 0 && routes.length === 0;
        } catch (_) {
            return false;
        }
    }

    function createWizard() {
        var overlay = document.createElement('div');
        overlay.className = 'onboarding-overlay';
        overlay.id = 'onboarding-wizard';
        overlay.innerHTML =
            '<div class="onboarding-modal">' +
                '<div class="onboarding-header">' +
                    '<div class="onboarding-icon"><i class="bi bi-rocket-takeoff"></i></div>' +
                    '<h3 class="onboarding-title">' + t('onboarding.title', 'Welcome!') + '</h3>' +
                    '<p class="onboarding-subtitle">' + t('onboarding.subtitle', 'Let\'s set up your gateway in 3 steps.') + '</p>' +
                '</div>' +
                '<div class="onboarding-steps">' +
                    '<div class="onboarding-step" data-step="1">' +
                        '<div class="step-number ' + (stepCompleted(1) ? 'done' : 'active') + '">' + (stepCompleted(1) ? '<i class="bi bi-check-lg"></i>' : '1') + '</div>' +
                        '<div class="step-info">' +
                            '<div class="step-title">' + t('onboarding.step1.title', 'Create a Cluster') + '</div>' +
                            '<div class="step-desc">' + t('onboarding.step1.desc', 'Add backend service nodes to a cluster.') + '</div>' +
                            '<a href="/' + getPrefix() + '/clusters" class="btn btn-primary btn-sm step-btn">' + t('onboarding.step1.button', 'Go to Clusters') + ' <i class="bi bi-arrow-right ms-1"></i></a>' +
                        '</div>' +
                    '</div>' +
                    '<div class="onboarding-step" data-step="2">' +
                        '<div class="step-number ' + (stepCompleted(2) ? 'done' : '') + '">' + (stepCompleted(2) ? '<i class="bi bi-check-lg"></i>' : '2') + '</div>' +
                        '<div class="step-info">' +
                            '<div class="step-title">' + t('onboarding.step2.title', 'Create a Route') + '</div>' +
                            '<div class="step-desc">' + t('onboarding.step2.desc', 'Define URL matching rules to forward requests to your cluster.') + '</div>' +
                            '<a href="/' + getPrefix() + '/routes" class="btn btn-primary btn-sm step-btn">' + t('onboarding.step2.button', 'Go to Routes') + ' <i class="bi bi-arrow-right ms-1"></i></a>' +
                        '</div>' +
                    '</div>' +
                    '<div class="onboarding-step" data-step="3">' +
                        '<div class="step-number ' + (stepCompleted(3) ? 'done' : '') + '">' + (stepCompleted(3) ? '<i class="bi bi-check-lg"></i>' : '3') + '</div>' +
                        '<div class="step-info">' +
                            '<div class="step-title">' + t('onboarding.step3.title', 'Verify Traffic') + '</div>' +
                            '<div class="step-desc">' + t('onboarding.step3.desc', 'Send a test request and check the logs.') + '</div>' +
                            '<a href="/' + getPrefix() + '/logs" class="btn btn-primary btn-sm step-btn">' + t('onboarding.step3.button', 'View Logs') + ' <i class="bi bi-arrow-right ms-1"></i></a>' +
                        '</div>' +
                    '</div>' +
                '</div>' +
                '<div class="onboarding-footer">' +
                    '<button class="btn btn-link btn-sm onboarding-skip">' + t('onboarding.skip', 'Skip, I\'ll configure later') + '</button>' +
                '</div>' +
            '</div>';
        document.body.appendChild(overlay);

        overlay.querySelector('.onboarding-skip').addEventListener('click', function() {
            localStorage.setItem('onboarding_completed', '1');
            overlay.remove();
        });

        overlay.addEventListener('click', function(e) {
            if (e.target === overlay) {
                localStorage.setItem('onboarding_completed', '1');
                overlay.remove();
            }
        });

        return overlay;
    }

    function stepCompleted(n) {
        try {
            return localStorage.getItem('onboarding_step_' + n) === '1';
        } catch (_) { return false; }
    }

    function markStepCompleted(n) {
        try { localStorage.setItem('onboarding_step_' + n, '1'); } catch (_) {}
    }

    // Check if current page completes a step
    function checkStepCompletion() {
        var page = document.body.getAttribute('data-page');
        if (page === 'clusters') markStepCompleted(1);
        if (page === 'routes') markStepCompleted(2);
        if (page === 'logs') markStepCompleted(3);

        // Check if all steps completed
        if (stepCompleted(1) && stepCompleted(2) && stepCompleted(3)) {
            try { localStorage.setItem('onboarding_completed', '1'); } catch (_) {}
            var wiz = document.getElementById('onboarding-wizard');
            if (wiz) wiz.remove();
        }
    }

    // Auto-show onboarding on overview page if needed
    async function init() {
        var page = document.body.getAttribute('data-page');
        if (page !== 'overview') {
            checkStepCompletion();
            return;
        }

        checkStepCompletion();

        var needsOnboarding = await checkNeedsOnboarding();
        if (needsOnboarding) {
            // Small delay to let page render
            setTimeout(createWizard, 800);
        }
    }

    // Public API
    window.DashboardOnboarding = {
        show: createWizard,
        init: init,
        markStep: markStepCompleted
    };

    // Auto-init on dashboard:ready
    document.addEventListener('dashboard:ready', function() {
        init();
    });
})();
