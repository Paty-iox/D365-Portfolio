(function() {
    'use strict';

    var claims = [];
    var filteredClaims = [];
    var sortColumn = 'createdon';
    var sortDirection = 'desc';

    var statusMap = {
        1: { label: 'Draft', class: 'status-draft' },
        100000000: { label: 'Submitted', class: 'status-submitted' },
        100000001: { label: 'Under Review', class: 'status-under-review' },
        100000002: { label: 'Approved', class: 'status-approved' },
        100000003: { label: 'Denied', class: 'status-denied' },
        100000004: { label: 'Closed', class: 'status-closed' }
    };

    var typeMap = {
        100000000: { label: 'Auto', class: 'type-auto' },
        100000001: { label: 'Home', class: 'type-home' },
        100000002: { label: 'Commercial', class: 'type-commercial' }
    };

    function init() {
        bindEvents();
        loadClaims();
    }

    function bindEvents() {
        document.getElementById('filter-status').addEventListener('change', applyFilters);
        document.getElementById('filter-type').addEventListener('change', applyFilters);

        // Sort headers
        document.querySelectorAll('.claims-table th[data-sort]').forEach(function(th) {
            th.addEventListener('click', function() {
                var column = this.getAttribute('data-sort');
                if (sortColumn === column) {
                    sortDirection = sortDirection === 'asc' ? 'desc' : 'asc';
                } else {
                    sortColumn = column;
                    sortDirection = 'asc';
                }
                updateSortIndicators();
                sortAndRender();
            });
        });
    }

    function getRequestVerificationToken() {
        var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        if (tokenInput && tokenInput.value) {
            return tokenInput.value;
        }
        return '';
    }

    function loadClaims() {
        var token = getRequestVerificationToken();
        var apiUrl = '/_api/new_claims?$select=new_claimid,new_claimnumber,new_claimtype,statuscode,new_incidentdate,new_estimatedamount,createdon&$orderby=createdon desc';

        fetch(apiUrl, {
            method: 'GET',
            headers: {
                'Content-Type': 'application/json',
                '__RequestVerificationToken': token
            }
        })
        .then(function(response) {
            if (!response.ok) throw new Error('Failed to load claims');
            return response.json();
        })
        .then(function(result) {
            claims = result.value || [];
            filteredClaims = claims.slice();
            updateStats();
            sortAndRender();
        })
        .catch(function(error) {
            console.error('Error loading claims:', error);
            document.getElementById('claims-tbody').innerHTML =
                '<tr><td colspan="6" style="text-align:center;color:#dc2626;padding:40px;">Failed to load claims. Please refresh the page.</td></tr>';
        });
    }

    function updateStats() {
        var total = claims.length;
        var open = claims.filter(function(c) {
            return c.statuscode === 100000000 || c.statuscode === 100000001;
        }).length;
        var approved = claims.filter(function(c) { return c.statuscode === 100000002; }).length;
        var denied = claims.filter(function(c) { return c.statuscode === 100000003; }).length;

        document.getElementById('stat-total').textContent = total;
        document.getElementById('stat-open').textContent = open;
        document.getElementById('stat-approved').textContent = approved;
        document.getElementById('stat-denied').textContent = denied;
    }

    function applyFilters() {
        var statusFilter = document.getElementById('filter-status').value;
        var typeFilter = document.getElementById('filter-type').value;

        filteredClaims = claims.filter(function(claim) {
            var statusMatch = !statusFilter || claim.statuscode == statusFilter;
            var typeMatch = !typeFilter || claim.new_claimtype == typeFilter;
            return statusMatch && typeMatch;
        });

        sortAndRender();
    }

    function updateSortIndicators() {
        document.querySelectorAll('.claims-table th[data-sort]').forEach(function(th) {
            th.classList.remove('sorted-asc', 'sorted-desc');
            if (th.getAttribute('data-sort') === sortColumn) {
                th.classList.add(sortDirection === 'asc' ? 'sorted-asc' : 'sorted-desc');
            }
        });
    }

    function sortAndRender() {
        filteredClaims.sort(function(a, b) {
            var aVal = a[sortColumn];
            var bVal = b[sortColumn];

            if (aVal === null || aVal === undefined) aVal = '';
            if (bVal === null || bVal === undefined) bVal = '';

            if (typeof aVal === 'string') {
                aVal = aVal.toLowerCase();
                bVal = bVal.toLowerCase();
            }

            var result = 0;
            if (aVal < bVal) result = -1;
            if (aVal > bVal) result = 1;

            return sortDirection === 'asc' ? result : -result;
        });

        renderTable();
    }

    function renderTable() {
        var tbody = document.getElementById('claims-tbody');
        var tableContainer = document.querySelector('.table-container');
        var emptyState = document.getElementById('empty-state');

        if (filteredClaims.length === 0) {
            tableContainer.style.display = 'none';
            emptyState.style.display = 'block';
            return;
        }

        tableContainer.style.display = 'block';
        emptyState.style.display = 'none';

        var html = filteredClaims.map(function(claim) {
            var claimNumber = claim.new_claimnumber || 'N/A';
            var claimType = typeMap[claim.new_claimtype] || { label: 'Unknown', class: '' };
            var status = statusMap[claim.statuscode] || { label: 'Unknown', class: '' };
            var incidentDate = formatDate(claim.new_incidentdate);
            var amount = formatCurrency(claim.new_estimatedamount);
            var createdDate = formatDate(claim.createdon);

            return '<tr>' +
                '<td><a href="/claim-detail?id=' + claim.new_claimid + '" class="claim-link">' + escapeHtml(claimNumber) + '</a></td>' +
                '<td><span class="type-badge ' + claimType.class + '">' + claimType.label + '</span></td>' +
                '<td><span class="status-badge ' + status.class + '">' + status.label + '</span></td>' +
                '<td>' + incidentDate + '</td>' +
                '<td class="amount">' + amount + '</td>' +
                '<td>' + createdDate + '</td>' +
            '</tr>';
        }).join('');

        tbody.innerHTML = html;
    }

    function formatDate(dateStr) {
        if (!dateStr) return '-';
        var date = new Date(dateStr);
        return date.toLocaleDateString('en-US', {
            month: 'short',
            day: 'numeric',
            year: 'numeric'
        });
    }

    function formatCurrency(value) {
        if (value === null || value === undefined) return '-';
        return '$' + parseFloat(value).toFixed(2).replace(/\d(?=(\d{3})+\.)/g, '$&,');
    }

    function escapeHtml(text) {
        if (!text) return '';
        var div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
