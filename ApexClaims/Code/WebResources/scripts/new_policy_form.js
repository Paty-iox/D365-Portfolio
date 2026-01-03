var ApexClaims = window.ApexClaims || {};
ApexClaims.Policy = ApexClaims.Policy || {};

(function () {
    "use strict";

    var COVERAGE_TYPES = {
        100000000: "Auto",
        100000001: "Home",
        100000002: "Commercial"
    };

    this.onLoad = function (executionContext) {
        var formContext = executionContext.getFormContext();

        var customerAttribute = formContext.getAttribute("new_customerid");
        var coverageTypeAttribute = formContext.getAttribute("new_coveragetype");

        if (customerAttribute) {
            customerAttribute.addOnChange(ApexClaims.Policy.setPolicyName);
        }

        if (coverageTypeAttribute) {
            coverageTypeAttribute.addOnChange(ApexClaims.Policy.setPolicyName);
        }

        this.setPolicyName(executionContext);
    };

    this.setPolicyName = function (executionContext) {
        var formContext = executionContext.getFormContext();

        var policyNameAttribute = formContext.getAttribute("new_policyname");
        if (!policyNameAttribute) {
            return;
        }

        var customerAttribute = formContext.getAttribute("new_customerid");
        var customerName = null;

        if (customerAttribute) {
            var customerValue = customerAttribute.getValue();
            if (customerValue && customerValue.length > 0) {
                customerName = customerValue[0].name;
                if (customerName) {
                    customerName = customerName.trim();
                }
            }
        }

        if (!customerName) {
            policyNameAttribute.setValue(null);
            return;
        }

        var coverageTypeAttribute = formContext.getAttribute("new_coveragetype");
        var coverageTypeLabel = null;

        if (coverageTypeAttribute) {
            var coverageTypeValue = coverageTypeAttribute.getValue();
            if (coverageTypeValue !== null && coverageTypeValue !== undefined) {
                coverageTypeLabel = COVERAGE_TYPES[coverageTypeValue] || null;
            }
        }

        var policyName;
        if (coverageTypeLabel) {
            policyName = customerName + " - " + coverageTypeLabel + " Policy";
        } else {
            policyName = customerName + " Policy";
        }

        policyNameAttribute.setValue(policyName);
    };

}).call(ApexClaims.Policy);
