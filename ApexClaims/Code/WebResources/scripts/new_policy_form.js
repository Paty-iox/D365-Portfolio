/**
 * Policy Form Script
 * Auto-populates the Policy Name field based on Customer and Coverage Type
 *
 * Web Resource: new_policy_form.js
 * Solution: DEMOSOLUTION1.0
 * Table: new_policy (Policy)
 */

var ApexClaims = window.ApexClaims || {};
ApexClaims.Policy = ApexClaims.Policy || {};

(function () {
    "use strict";

    // Coverage Type option set values
    var COVERAGE_TYPES = {
        100000000: "Auto",
        100000001: "Home",
        100000002: "Commercial"
    };

    /**
     * Form OnLoad event handler
     * Registers onChange handlers and sets initial Policy Name
     * @param {Object} executionContext - The execution context passed by the form
     */
    this.onLoad = function (executionContext) {
        var formContext = executionContext.getFormContext();

        // Register onChange handlers for Customer and Coverage Type fields
        var customerAttribute = formContext.getAttribute("new_customerid");
        var coverageTypeAttribute = formContext.getAttribute("new_coveragetype");

        if (customerAttribute) {
            customerAttribute.addOnChange(ApexClaims.Policy.setPolicyName);
        }

        if (coverageTypeAttribute) {
            coverageTypeAttribute.addOnChange(ApexClaims.Policy.setPolicyName);
        }

        // Set initial Policy Name for pre-populated values
        this.setPolicyName(executionContext);
    };

    /**
     * Sets the Policy Name based on Customer Name and Coverage Type
     * Format: "{Customer Name} - {Coverage Type} Policy" or "{Customer Name} Policy"
     * @param {Object} executionContext - The execution context passed by the form
     */
    this.setPolicyName = function (executionContext) {
        var formContext = executionContext.getFormContext();

        // Get the Policy Name attribute
        var policyNameAttribute = formContext.getAttribute("new_policyname");
        if (!policyNameAttribute) {
            return;
        }

        // Get the Customer lookup value
        var customerAttribute = formContext.getAttribute("new_customerid");
        var customerName = null;

        if (customerAttribute) {
            var customerValue = customerAttribute.getValue();
            if (customerValue && customerValue.length > 0) {
                customerName = customerValue[0].name;
                // Trim whitespace from customer name
                if (customerName) {
                    customerName = customerName.trim();
                }
            }
        }

        // If no customer name, clear the Policy Name and exit
        if (!customerName) {
            policyNameAttribute.setValue(null);
            return;
        }

        // Get the Coverage Type value
        var coverageTypeAttribute = formContext.getAttribute("new_coveragetype");
        var coverageTypeLabel = null;

        if (coverageTypeAttribute) {
            var coverageTypeValue = coverageTypeAttribute.getValue();
            if (coverageTypeValue !== null && coverageTypeValue !== undefined) {
                coverageTypeLabel = COVERAGE_TYPES[coverageTypeValue] || null;
            }
        }

        // Build the Policy Name
        var policyName;
        if (coverageTypeLabel) {
            policyName = customerName + " - " + coverageTypeLabel + " Policy";
        } else {
            policyName = customerName + " Policy";
        }

        // Set the Policy Name
        policyNameAttribute.setValue(policyName);
    };

}).call(ApexClaims.Policy);
