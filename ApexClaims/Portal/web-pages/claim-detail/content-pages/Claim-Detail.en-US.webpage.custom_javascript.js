(function() {
    'use strict';

    var claimId = null;
    var claimData = null;

    var statusMap = {
        1: { label: 'Draft', class: 'status-draft' },
        2: { label: 'Denied', class: 'status-denied' },
        100000001: { label: 'Under Review', class: 'status-under-review' },
        100000002: { label: 'Fraud Review', class: 'status-fraud-review' },
        100000003: { label: 'Submitted', class: 'status-submitted' },
        100000004: { label: 'Approved', class: 'status-approved' },
        100000005: { label: 'Closed', class: 'status-closed' }
    };

    var typeMap = {
        100000000: 'Auto',
        100000001: 'Home',
        100000002: 'Commercial'
    };

    var coverageTypeMap = {
        100000000: 'Comprehensive',
        100000001: 'Collision',
        100000002: 'Liability',
        100000003: 'Full Coverage'
    };


    function init() {
        claimId = getClaimIdFromUrl();

        if (!claimId) {
            showError('No claim ID provided. Please select a claim from the dashboard.');
            return;
        }

        loadClaimDetails();
    }

    function getClaimIdFromUrl() {
        var urlParams = new URLSearchParams(window.location.search);
        return urlParams.get('id');
    }

    function getRequestVerificationToken() {
        // Try multiple methods to find the token

        // Method 1: Standard input
        var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        if (tokenInput && tokenInput.value) {
            console.log('Token found via input');
            return tokenInput.value;
        }

        // Method 2: Try any form's token
        var formToken = document.querySelector('form input[name="__RequestVerificationToken"]');
        if (formToken && formToken.value) {
            console.log('Token found via form');
            return formToken.value;
        }

        // Method 3: Try meta tag
        var metaToken = document.querySelector('meta[name="csrf-token"]');
        if (metaToken && metaToken.content) {
            console.log('Token found via meta');
            return metaToken.content;
        }

        // Method 4: Try shell object (Power Pages)
        if (typeof shell !== 'undefined' && shell.getTokenDeferred) {
            console.log('Shell object found');
        }

        // Method 5: Check window object
        if (window.__RequestVerificationToken) {
            console.log('Token found via window object');
            return window.__RequestVerificationToken;
        }

        console.log('No token found - proceeding without it');
        return '';
    }

    function showError(message) {
        document.getElementById('loading-container').style.display = 'none';
        document.getElementById('claim-content').style.display = 'none';
        document.getElementById('error-container').style.display = 'block';
        document.getElementById('error-message').textContent = message;
    }

    function showContent() {
        document.getElementById('loading-container').style.display = 'none';
        document.getElementById('error-container').style.display = 'none';
        document.getElementById('claim-content').style.display = 'block';
    }

    function loadClaimDetails() {
        var token = getRequestVerificationToken();

        // Fetch all claims and filter in JavaScript
        // Use new_policyidtext (text field) instead of lookup - lookup fields cause 403
        var apiUrl = '/_api/new_claims?$select=new_claimid,new_claimnumber,new_claimtype,statuscode,new_incidentdate,new_incidentlocation,new_description,new_estimatedamount,new_approvedamount,createdon,new_policyidtext,new_customeridtext&$orderby=createdon desc';

        fetch(apiUrl, {
            method: 'GET',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                '__RequestVerificationToken': token
            }
        })
        .then(function(response) {
            if (!response.ok) {
                throw new Error('Failed to load claim details.');
            }
            return response.json();
        })
        .then(function(result) {
            // Find the claim with matching ID
            var claims = result.value || [];
            var data = null;
            for (var i = 0; i < claims.length; i++) {
                if (claims[i].new_claimid === claimId) {
                    data = claims[i];
                    break;
                }
            }

            if (!data) {
                throw new Error('Claim not found or you do not have permission to view it.');
            }

            claimData = data;
            console.log('Claim data:', data);

            // Load policy details if policy exists (using text field workaround)
            var policyId = data.new_policyidtext;
            var customerId = data.new_customeridtext;

            if (policyId) {
                return loadPolicyDetails(policyId, token).then(function() {
                    if (customerId) {
                        return loadCustomerDetails(customerId, token);
                    }
                });
            } else if (customerId) {
                return loadCustomerDetails(customerId, token);
            }
            return Promise.resolve();
        })
        .then(function() {
            renderClaimDetails();
            loadSignature();
            showContent();
        })
        .catch(function(error) {
            console.error('Error loading claim:', error);
            showError(error.message || 'Failed to load claim details. Please try again.');
        });
    }

    function loadPolicyDetails(policyId, token) {
        // Fetch all policies and filter in JS (must include $select)
        var apiUrl = '/_api/new_policies?$select=new_policyid,new_policynumber,new_coveragetype,new_coveragelimit,new_deductibleamount';

        return fetch(apiUrl, {
            method: 'GET',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                '__RequestVerificationToken': token
            }
        })
        .then(function(response) {
            if (!response.ok) return null;
            return response.json();
        })
        .then(function(result) {
            if (result && result.value) {
                // Find matching policy
                for (var i = 0; i < result.value.length; i++) {
                    if (result.value[i].new_policyid === policyId) {
                        claimData.policyData = result.value[i];
                        console.log('Policy data:', result.value[i]);
                        break;
                    }
                }
            }
            return Promise.resolve();
        })
        .catch(function(error) {
            console.error('Error loading policy:', error);
            return Promise.resolve();
        });
    }

    function loadCustomerDetails(customerId, token) {
        // Fetch all contacts and filter in JS (Power Pages blocks $filter)
        var apiUrl = '/_api/contacts?$select=contactid,firstname,lastname';

        return fetch(apiUrl, {
            method: 'GET',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                '__RequestVerificationToken': token
            }
        })
        .then(function(response) {
            if (!response.ok) return null;
            return response.json();
        })
        .then(function(result) {
            if (result && result.value) {
                // Find matching contact
                for (var i = 0; i < result.value.length; i++) {
                    if (result.value[i].contactid === customerId) {
                        claimData.customerData = result.value[i];
                        console.log('Customer data:', result.value[i]);
                        break;
                    }
                }
            }
        })
        .catch(function(error) {
            console.error('Error loading customer:', error);
        });
    }

    function renderClaimDetails() {
        // Header
        document.getElementById('claim-number').textContent = claimData.new_claimnumber || 'N/A';
        document.getElementById('created-date').textContent = 'Created ' + formatDate(claimData.createdon);

        // Policy Information
        if (claimData.policyData) {
            var policy = claimData.policyData;
            document.getElementById('policy-number').textContent = policy.new_policynumber || '-';
            document.getElementById('coverage-type').textContent = coverageTypeMap[policy.new_coveragetype] || '-';
            document.getElementById('coverage-limit').textContent = formatCurrency(policy.new_coveragelimit);
            document.getElementById('deductible').textContent = formatCurrency(policy.new_deductibleamount);
        }

        // Customer Information (from OOTB Contact table)
        if (claimData.customerData) {
            var customer = claimData.customerData;
            var fullName = [customer.firstname, customer.lastname].filter(Boolean).join(' ');
            document.getElementById('customer-name').textContent = fullName || '-';
        }

        // Incident Details
        document.getElementById('incident-date').textContent = formatDateTime(claimData.new_incidentdate);
        document.getElementById('incident-location').textContent = claimData.new_incidentlocation || '-';
        document.getElementById('claim-type').textContent = typeMap[claimData.new_claimtype] || '-';
        document.getElementById('description').textContent = claimData.new_description || '-';

        // Financial Summary
        document.getElementById('estimated-amount').textContent = formatCurrency(claimData.new_estimatedamount);
        document.getElementById('approved-amount').textContent = formatCurrency(claimData.new_approvedamount);

        // Calculate deductible applied (if approved)
        if (claimData.statuscode === 100000004 && claimData.policyData) {
            document.getElementById('deductible-applied').textContent = formatCurrency(claimData.policyData.new_deductibleamount);
        } else {
            document.getElementById('deductible-applied').textContent = '-';
        }

        // Payment status based on claim status
        var paymentStatusMap = {
            1: 'Not Started',           // Draft
            2: 'Not Applicable',        // Denied
            100000001: 'Pending Review', // Under Review
            100000002: 'Under Investigation', // Fraud Review
            100000003: 'Pending Review', // Submitted
            100000004: 'Pending Payment', // Approved
            100000005: 'Paid'            // Closed
        };
        document.getElementById('payment-status').textContent = paymentStatusMap[claimData.statuscode] || '-';
    }

    function loadSignature() {
        // Note: Direct GUID access may fail with 403 - signature loading is optional
        var signatureUrl = '/_api/new_claims(' + claimId + ')/new_digitalsignature/$value';
        var token = getRequestVerificationToken();

        fetch(signatureUrl, {
            method: 'GET',
            credentials: 'same-origin',
            headers: {
                '__RequestVerificationToken': token
            }
        })
        .then(function(response) {
            if (response.ok) {
                return response.blob();
            }
            throw new Error('No signature');
        })
        .then(function(blob) {
            if (blob.size > 0) {
                var url = URL.createObjectURL(blob);
                document.getElementById('signature-image').src = url;
                document.getElementById('signature-section').style.display = 'block';
            }
        })
        .catch(function(error) {
            // No signature uploaded - this is fine
        });
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

    function formatDateTime(dateStr) {
        if (!dateStr) return '-';
        var date = new Date(dateStr);
        return date.toLocaleDateString('en-US', {
            month: 'short',
            day: 'numeric',
            year: 'numeric'
        }) + ' at ' + date.toLocaleTimeString('en-US', {
            hour: 'numeric',
            minute: '2-digit',
            hour12: true
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
