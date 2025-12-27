(function() {
    'use strict';

    var config = {
        currentStep: 1,
        totalSteps: 5,
        claimId: null,
        policyData: null,
        autoSaveInterval: 60000,
        autoSaveTimer: null,
        isDirty: false,
        photos: [],
        docs: [],
        hasSignature: false
    };

    var elements = {};

    function init() {
        cacheElements();
        bindEvents();
        initSignaturePad();
        initFileUpload();
        console.log('Claim Wizard initialized');
    }

    function cacheElements() {
        elements.wizard = document.getElementById('claim-wizard');
        elements.stepPanels = document.querySelectorAll('.step-panel');
        elements.progressSteps = document.querySelectorAll('.progress-step');
        elements.progressLines = document.querySelectorAll('.progress-line');
        elements.btnPrevious = document.getElementById('btn-previous');
        elements.btnNext = document.getElementById('btn-next');
        elements.btnSubmit = document.getElementById('btn-submit');

        elements.policyNumber = document.getElementById('policy-number');
        elements.policySpinner = document.getElementById('policy-spinner');
        elements.policyStatus = document.getElementById('policy-status');
        elements.policyError = document.getElementById('policy-error');
        elements.policyCard = document.getElementById('policy-card');
        elements.incidentDate = document.getElementById('incident-date');
        elements.incidentTime = document.getElementById('incident-time');
        elements.claimType = document.getElementById('claim-type');
        elements.incidentLocation = document.getElementById('incident-location');
        elements.description = document.getElementById('description');
        elements.estimatedAmount = document.getElementById('estimated-amount');
        elements.consentCheckbox = document.getElementById('consent-checkbox');
        elements.signaturePad = document.getElementById('signature-pad');
    }

    function bindEvents() {
        elements.btnPrevious.addEventListener('click', goToPreviousStep);
        elements.btnNext.addEventListener('click', goToNextStep);
        elements.btnSubmit.addEventListener('click', submitClaim);

        document.querySelectorAll('.btn-edit').forEach(function(btn) {
            btn.addEventListener('click', function() {
                var step = parseInt(this.getAttribute('data-goto-step'));
                goToStep(step);
            });
        });

        if (elements.policyNumber) {
            var debounceTimer;
            elements.policyNumber.addEventListener('input', function() {
                clearTimeout(debounceTimer);
                debounceTimer = setTimeout(function() {
                    validatePolicy();
                }, 500);
            });
        }

        if (elements.description) {
            elements.description.addEventListener('input', function() {
                document.getElementById('description-count').textContent = this.value.length;
            });
        }

        document.getElementById('clear-signature').addEventListener('click', clearSignature);
    }

    function goToStep(step) {
        if (step < 1 || step > config.totalSteps) return;

        config.currentStep = step;

        elements.stepPanels.forEach(function(panel) {
            panel.classList.remove('active');
            if (parseInt(panel.getAttribute('data-step')) === step) {
                panel.classList.add('active');
            }
        });

        updateProgressIndicator();
        updateNavigationButtons();

        if (step === 5) {
            populateReviewStep();
        }

        elements.wizard.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }

    function goToPreviousStep() {
        if (config.currentStep > 1) {
            goToStep(config.currentStep - 1);
        }
    }

    function goToNextStep() {
        if (!validateStep(config.currentStep)) {
            return;
        }

        if (config.currentStep < config.totalSteps) {
            goToStep(config.currentStep + 1);
        }
    }

    function updateProgressIndicator() {
        elements.progressSteps.forEach(function(stepEl, index) {
            var stepNum = index + 1;
            stepEl.classList.remove('active', 'completed');

            if (stepNum === config.currentStep) {
                stepEl.classList.add('active');
            } else if (stepNum < config.currentStep) {
                stepEl.classList.add('completed');
                stepEl.querySelector('.step-circle').innerHTML = '✓';
            } else {
                stepEl.querySelector('.step-circle').innerHTML = stepNum;
            }
        });

        elements.progressLines.forEach(function(line, index) {
            if (index < config.currentStep - 1) {
                line.classList.add('completed');
            } else {
                line.classList.remove('completed');
            }
        });
    }

    function updateNavigationButtons() {
        elements.btnPrevious.disabled = config.currentStep === 1;

        if (config.currentStep === config.totalSteps) {
            elements.btnNext.style.display = 'none';
            elements.btnSubmit.style.display = 'inline-flex';
        } else {
            elements.btnNext.style.display = 'inline-flex';
            elements.btnSubmit.style.display = 'none';
        }
    }

    function validateStep(step) {
        clearAllErrors();
        var isValid = true;

        switch(step) {
            case 1:
                if (!config.policyData) {
                    showError('policy-error', 'Please enter and validate your policy number');
                    isValid = false;
                }
                break;
            case 2:
                if (!elements.incidentDate.value) {
                    showError('incident-date-error', 'Incident date is required');
                    isValid = false;
                }
                if (!elements.incidentTime.value) {
                    showError('incident-time-error', 'Incident time is required');
                    isValid = false;
                }
                if (!elements.claimType.value) {
                    showError('claim-type-error', 'Please select a claim type');
                    isValid = false;
                }
                if (!elements.incidentLocation.value.trim()) {
                    showError('incident-location-error', 'Incident location is required');
                    isValid = false;
                }
                break;
            case 3:
                if (!elements.description.value.trim()) {
                    showError('description-error', 'Description is required');
                    isValid = false;
                } else if (elements.description.value.trim().length < 50) {
                    showError('description-error', 'Please provide at least 50 characters');
                    isValid = false;
                }
                if (!elements.estimatedAmount.value) {
                    showError('estimated-amount-error', 'Estimated amount is required');
                    isValid = false;
                } else if (parseFloat(elements.estimatedAmount.value) < 0) {
                    showError('estimated-amount-error', 'Estimated amount cannot be negative');
                    isValid = false;
                }
                break;
            case 4:
                if (config.photos.length === 0) {
                    showError('photo-error', 'At least one damage photo is required');
                    isValid = false;
                }
                break;
            case 5:
                if (!config.hasSignature) {
                    showError('signature-error', 'Please provide your signature');
                    isValid = false;
                }
                if (!elements.consentCheckbox.checked) {
                    showError('consent-error', 'You must agree to the certification statement');
                    isValid = false;
                }
                break;
        }

        return isValid;
    }

    function showError(elementId, message) {
        var errorEl = document.getElementById(elementId);
        if (errorEl) {
            errorEl.textContent = message;
            errorEl.classList.add('show');
        }
    }

    function clearAllErrors() {
        document.querySelectorAll('.field-error').forEach(function(el) {
            el.textContent = '';
            el.classList.remove('show');
        });
    }

    function validatePolicy() {
        var policyNumber = elements.policyNumber.value.trim();
        if (!policyNumber) {
            config.policyData = null;
            elements.policyCard.style.display = 'none';
            elements.policyError.textContent = '';
            elements.policyError.classList.remove('show');
            return;
        }

        // Clear error and status immediately when starting validation
        elements.policyError.textContent = '';
        elements.policyError.classList.remove('show');
        elements.policySpinner.style.display = 'inline';
        elements.policyStatus.innerHTML = '';

        var token = getRequestVerificationToken();
        // Query policy with primary key and _new_customerid_value for lookup (returns formatted name via OData annotation)
        var apiUrl = '/_api/new_policies?$select=new_policyid,new_policynumber,new_coveragetype,new_coveragelimit,new_deductibleamount,new_effectivestartdate,new_effectiveenddate,_new_customerid_value&$filter=new_policynumber eq \'' + encodeURIComponent(policyNumber) + '\'';

        fetch(apiUrl, {
            method: 'GET',
            headers: {
                'Content-Type': 'application/json',
                '__RequestVerificationToken': token,
                'Prefer': 'odata.include-annotations="*"'
            }
        })
        .then(function(response) {
            if (!response.ok) throw new Error('API request failed');
            return response.json();
        })
        .then(function(result) {
            elements.policySpinner.style.display = 'none';

            if (result.value && result.value.length > 0) {
                var policy = result.value[0];
                config.policyData = policy;
                displayPolicyCard(policy);
                elements.policyStatus.innerHTML = '✓';
                elements.policyStatus.className = 'input-status valid';
                elements.policyNumber.classList.add('valid');
                elements.policyNumber.classList.remove('error');
                // Clear any previous error message
                elements.policyError.textContent = '';
                elements.policyError.classList.remove('show');

                if (policy.new_coveragetype) {
                    elements.claimType.value = policy.new_coveragetype;
                }
            } else {
                config.policyData = null;
                elements.policyCard.style.display = 'none';
                elements.policyStatus.innerHTML = '✕';
                elements.policyStatus.className = 'input-status invalid';
                elements.policyNumber.classList.add('error');
                elements.policyNumber.classList.remove('valid');
                showError('policy-error', 'Policy not found');
            }
        })
        .catch(function(error) {
            console.error('Policy validation error:', error);
            elements.policySpinner.style.display = 'none';
            showError('policy-error', 'Unable to validate policy');
        });
    }

    function displayPolicyCard(policy) {
        var coverageTypes = { 100000000: 'Auto', 100000001: 'Home', 100000002: 'Commercial' };

        // Get customer name from OData annotation
        var customerName = policy['_new_customerid_value@OData.Community.Display.V1.FormattedValue'] || 'Policy Holder';
        document.getElementById('policy-customer-name').textContent = customerName;
        document.getElementById('policy-coverage-type').textContent = coverageTypes[policy.new_coveragetype] || '-';
        document.getElementById('policy-coverage-limit').textContent = '$' + formatCurrency(policy.new_coveragelimit || 0);
        document.getElementById('policy-deductible').textContent = '$' + formatCurrency(policy.new_deductibleamount || 0);
        document.getElementById('policy-effective-period').textContent = formatDate(policy.new_effectivestartdate) + ' - ' + formatDate(policy.new_effectiveenddate);

        var statusEl = document.getElementById('policy-card-status');
        statusEl.textContent = 'Active';
        statusEl.className = 'policy-card-status active';

        elements.policyCard.style.display = 'block';
    }

    function initFileUpload() {
        var photoZone = document.getElementById('photo-upload-zone');
        var photoInput = document.getElementById('photo-input');
        var docZone = document.getElementById('doc-upload-zone');
        var docInput = document.getElementById('doc-input');

        photoZone.addEventListener('click', function() { photoInput.click(); });
        docZone.addEventListener('click', function() { docInput.click(); });

        photoZone.addEventListener('dragover', function(e) { e.preventDefault(); this.classList.add('drag-over'); });
        photoZone.addEventListener('dragleave', function() { this.classList.remove('drag-over'); });
        photoZone.addEventListener('drop', function(e) { e.preventDefault(); this.classList.remove('drag-over'); handleFiles(e.dataTransfer.files, 'photo'); });

        docZone.addEventListener('dragover', function(e) { e.preventDefault(); this.classList.add('drag-over'); });
        docZone.addEventListener('dragleave', function() { this.classList.remove('drag-over'); });
        docZone.addEventListener('drop', function(e) { e.preventDefault(); this.classList.remove('drag-over'); handleFiles(e.dataTransfer.files, 'doc'); });

        photoInput.addEventListener('change', function() { handleFiles(this.files, 'photo'); this.value = ''; });
        docInput.addEventListener('change', function() { handleFiles(this.files, 'doc'); this.value = ''; });
    }

    function handleFiles(files, type) {
        var preview = document.getElementById(type + '-preview');
        var fileArray = type === 'photo' ? config.photos : config.docs;

        for (var i = 0; i < files.length; i++) {
            var file = files[i];
            if (fileArray.length >= 10) break;

            var fileObj = {
                id: 'file_' + Date.now() + '_' + i,
                file: file,
                name: file.name
            };
            fileArray.push(fileObj);

            var item = document.createElement('div');
            item.className = 'upload-item';
            item.id = 'upload-' + fileObj.id;

            if (file.type.startsWith('image/')) {
                var img = document.createElement('img');
                var reader = new FileReader();
                reader.onload = (function(imgEl) {
                    return function(e) { imgEl.src = e.target.result; };
                })(img);
                reader.readAsDataURL(file);
                item.appendChild(img);
            } else {
                var icon = document.createElement('div');
                icon.className = 'file-icon';
                icon.textContent = '📄';
                item.appendChild(icon);
            }

            var name = document.createElement('span');
            name.className = 'file-name';
            name.textContent = file.name.length > 15 ? file.name.substring(0, 12) + '...' : file.name;
            item.appendChild(name);

            var removeBtn = document.createElement('button');
            removeBtn.className = 'remove-btn';
            removeBtn.innerHTML = '×';
            removeBtn.onclick = (function(fid, ftype) {
                return function(e) {
                    e.stopPropagation();
                    removeFile(fid, ftype);
                };
            })(fileObj.id, type);
            item.appendChild(removeBtn);

            preview.appendChild(item);
        }
    }

    function removeFile(fileId, type) {
        var fileArray = type === 'photo' ? config.photos : config.docs;
        var index = fileArray.findIndex(function(f) { return f.id === fileId; });
        if (index > -1) fileArray.splice(index, 1);

        var item = document.getElementById('upload-' + fileId);
        if (item) item.remove();
    }

    function initSignaturePad() {
        var canvas = elements.signaturePad;
        if (!canvas) return;

        var ctx = canvas.getContext('2d');
        var isDrawing = false;
        var lastX = 0, lastY = 0;

        ctx.strokeStyle = '#000000';
        ctx.lineWidth = 2;
        ctx.lineCap = 'round';
        ctx.fillStyle = '#ffffff';
        ctx.fillRect(0, 0, canvas.width, canvas.height);

        function getPos(e) {
            var rect = canvas.getBoundingClientRect();
            var clientX = e.clientX || (e.touches && e.touches[0].clientX);
            var clientY = e.clientY || (e.touches && e.touches[0].clientY);
            return {
                x: (clientX - rect.left) * (canvas.width / rect.width),
                y: (clientY - rect.top) * (canvas.height / rect.height)
            };
        }

        canvas.addEventListener('mousedown', function(e) {
            isDrawing = true;
            var pos = getPos(e);
            lastX = pos.x;
            lastY = pos.y;
        });

        canvas.addEventListener('mousemove', function(e) {
            if (!isDrawing) return;
            var pos = getPos(e);
            ctx.beginPath();
            ctx.moveTo(lastX, lastY);
            ctx.lineTo(pos.x, pos.y);
            ctx.stroke();
            lastX = pos.x;
            lastY = pos.y;
            config.hasSignature = true;
        });

        canvas.addEventListener('mouseup', function() { isDrawing = false; });
        canvas.addEventListener('mouseout', function() { isDrawing = false; });

        canvas.addEventListener('touchstart', function(e) {
            e.preventDefault();
            isDrawing = true;
            var pos = getPos(e);
            lastX = pos.x;
            lastY = pos.y;
        });

        canvas.addEventListener('touchmove', function(e) {
            e.preventDefault();
            if (!isDrawing) return;
            var pos = getPos(e);
            ctx.beginPath();
            ctx.moveTo(lastX, lastY);
            ctx.lineTo(pos.x, pos.y);
            ctx.stroke();
            lastX = pos.x;
            lastY = pos.y;
            config.hasSignature = true;
        });

        canvas.addEventListener('touchend', function() { isDrawing = false; });
    }

    function clearSignature() {
        var canvas = elements.signaturePad;
        var ctx = canvas.getContext('2d');
        ctx.fillStyle = '#ffffff';
        ctx.fillRect(0, 0, canvas.width, canvas.height);
        config.hasSignature = false;
    }

    function populateReviewStep() {
        var coverageTypes = { 100000000: 'Auto', 100000001: 'Home', 100000002: 'Commercial' };

        var reviewPolicy = document.getElementById('review-policy');
        if (config.policyData) {
            reviewPolicy.innerHTML =
                '<div class="review-row"><span class="review-label">Policy Number:</span><span class="review-value">' + (config.policyData.new_policynumber || '-') + '</span></div>' +
                '<div class="review-row"><span class="review-label">Customer:</span><span class="review-value">' + (config.policyData['_new_customerid_value@OData.Community.Display.V1.FormattedValue'] || 'Policy Holder') + '</span></div>' +
                '<div class="review-row"><span class="review-label">Coverage Type:</span><span class="review-value">' + (coverageTypes[config.policyData.new_coveragetype] || '-') + '</span></div>';
        }

        var reviewIncident = document.getElementById('review-incident');
        reviewIncident.innerHTML =
            '<div class="review-row"><span class="review-label">Date & Time:</span><span class="review-value">' + formatDate(elements.incidentDate.value) + ' at ' + elements.incidentTime.value + '</span></div>' +
            '<div class="review-row"><span class="review-label">Claim Type:</span><span class="review-value">' + (coverageTypes[elements.claimType.value] || '-') + '</span></div>' +
            '<div class="review-row"><span class="review-label">Location:</span><span class="review-value">' + escapeHtml(elements.incidentLocation.value) + '</span></div>';

        var reviewDescription = document.getElementById('review-description');
        reviewDescription.innerHTML =
            '<div class="review-row"><span class="review-label">Description:</span><span class="review-value">' + escapeHtml(elements.description.value) + '</span></div>' +
            '<div class="review-row"><span class="review-label">Estimated Amount:</span><span class="review-value">$' + formatCurrency(elements.estimatedAmount.value) + '</span></div>';

        var reviewDocuments = document.getElementById('review-documents');
        reviewDocuments.innerHTML =
            '<div class="review-row"><span class="review-label">Damage Photos:</span><span class="review-value">' + config.photos.length + ' file(s)</span></div>' +
            '<div class="review-row"><span class="review-label">Other Documents:</span><span class="review-value">' + config.docs.length + ' file(s)</span></div>';
    }

    function submitClaim() {
        if (!validateStep(5)) return;

        elements.btnSubmit.disabled = true;
        elements.btnSubmit.innerHTML = '⟳ Submitting...';

        var token = getRequestVerificationToken();
        var claimId = null;

        // Build claim data
        var claimData = {
            'new_incidentdate': elements.incidentDate.value + 'T' + elements.incidentTime.value + ':00Z',
            'new_claimtype': parseInt(elements.claimType.value),
            'new_incidentlocation': elements.incidentLocation.value,
            'new_description': elements.description.value,
            'new_estimatedamount': parseFloat(elements.estimatedAmount.value),
            'statuscode': 100000003  // Submitted
        };

        // Store policy GUID in text field (Power Automate will set the actual lookup)
        if (config.policyData.new_policyid) {
            claimData['new_policyidtext'] = config.policyData.new_policyid;
        }

        // Store customer GUID in text field (Power Automate will set the actual lookup)
        if (config.policyData._new_customerid_value) {
            claimData['new_customeridtext'] = config.policyData._new_customerid_value;
        }

        console.log('Submitting claim data:', JSON.stringify(claimData, null, 2));

        // Step 1: Create the claim
        fetch('/_api/new_claims', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                '__RequestVerificationToken': token
            },
            body: JSON.stringify(claimData)
        })
        .then(function(response) {
            console.log('Response status:', response.status);
            if (!response.ok) {
                return response.text().then(function(text) {
                    console.error('API Error Response:', text);
                    try {
                        var err = JSON.parse(text);
                        throw new Error(err.error ? err.error.message : 'Failed to submit claim');
                    } catch (e) {
                        throw new Error(text || 'Failed to submit claim');
                    }
                });
            }
            // Extract claim ID from OData-EntityId header
            var entityId = response.headers.get('OData-EntityId');
            if (entityId) {
                var match = entityId.match(/\(([^)]+)\)/);
                if (match) {
                    claimId = match[1];
                    console.log('Claim ID:', claimId);
                }
            }
            return response.text();
        })
        .then(function() {
            // Step 2: Upload signature if we have a claim ID
            if (claimId && config.hasSignature) {
                elements.btnSubmit.innerHTML = '⟳ Uploading signature...';
                return uploadSignature(claimId, token);
            }
            return Promise.resolve();
        })
        .then(function() {
            // Step 3: Upload files if we have a claim ID
            if (claimId && (config.photos.length > 0 || config.docs.length > 0)) {
                elements.btnSubmit.innerHTML = '⟳ Uploading files...';
                return uploadAllFiles(claimId, token);
            }
            return Promise.resolve();
        })
        .then(function() {
            console.log('Claim and files submitted successfully');
            alert('Your claim has been submitted successfully!');
            window.location.href = '/';
        })
        .catch(function(error) {
            console.error('Claim submission error:', error);
            alert('Error submitting claim: ' + error.message);
            elements.btnSubmit.disabled = false;
            elements.btnSubmit.innerHTML = '✈ Submit Claim';
        });
    }

    function uploadSignature(claimId, token) {
        var canvas = elements.signaturePad;

        // Convert canvas to blob
        return new Promise(function(resolve, reject) {
            canvas.toBlob(function(blob) {
                if (!blob) {
                    reject(new Error('Failed to capture signature'));
                    return;
                }

                console.log('Uploading signature...');

                // Upload signature to claim's image column
                fetch('/_api/new_claims(' + claimId + ')/new_digitalsignature', {
                    method: 'PATCH',
                    headers: {
                        'Content-Type': 'application/octet-stream',
                        '__RequestVerificationToken': token,
                        'x-ms-file-name': 'signature.png'
                    },
                    body: blob
                })
                .then(function(response) {
                    if (!response.ok) {
                        return response.text().then(function(text) {
                            console.error('Signature upload error:', text);
                            reject(new Error('Failed to upload signature'));
                        });
                    }
                    console.log('Signature uploaded successfully');
                    resolve();
                })
                .catch(reject);
            }, 'image/png');
        });
    }

    function uploadAllFiles(claimId, token) {
        var allFiles = [];

        // Add photos with type 100000000 (Photo)
        config.photos.forEach(function(photo) {
            allFiles.push({ file: photo.file, name: photo.name, type: 100000000 });
        });

        // Add docs with type 100000001 (Document)
        config.docs.forEach(function(doc) {
            allFiles.push({ file: doc.file, name: doc.name, type: 100000001 });
        });

        console.log('Uploading', allFiles.length, 'files');

        // Upload files sequentially
        return allFiles.reduce(function(promise, fileObj, index) {
            return promise.then(function() {
                console.log('Uploading file', index + 1, 'of', allFiles.length, ':', fileObj.name);
                return uploadSingleFile(claimId, fileObj, token);
            });
        }, Promise.resolve());
    }

    function uploadSingleFile(claimId, fileObj, token) {
        // Step 1: Create document record
        var docData = {
            'new_documentname': fileObj.name,
            'new_documenttype': fileObj.type,
            'new_claimidtext': claimId,  // Text field - Power Automate will set actual lookup
            'new_filesize': fileObj.file.size,  // File size in bytes
            'new_mimetype': fileObj.file.type   // MIME type (e.g., "image/jpeg")
        };

        var documentId = null;

        return fetch('/_api/new_claimdocuments', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                '__RequestVerificationToken': token
            },
            body: JSON.stringify(docData)
        })
        .then(function(response) {
            if (!response.ok) {
                return response.text().then(function(text) {
                    throw new Error('Failed to create document record: ' + text);
                });
            }
            // Extract document ID from response
            var entityId = response.headers.get('OData-EntityId');
            if (entityId) {
                var match = entityId.match(/\(([^)]+)\)/);
                if (match) {
                    documentId = match[1];
                    console.log('Document record created:', documentId);
                }
            }
            return response.text();
        })
        .then(function() {
            if (!documentId) {
                throw new Error('Could not get document ID');
            }
            // Step 2: Upload file content
            return readFileAsBase64(fileObj.file);
        })
        .then(function(base64Content) {
            // Upload file to the file column
            return fetch('/_api/new_claimdocuments(' + documentId + ')/new_file', {
                method: 'PATCH',
                headers: {
                    'Content-Type': 'application/octet-stream',
                    '__RequestVerificationToken': token,
                    'x-ms-file-name': encodeURIComponent(fileObj.name)
                },
                body: base64ToBlob(base64Content, fileObj.file.type)
            });
        })
        .then(function(response) {
            if (!response.ok) {
                return response.text().then(function(text) {
                    console.error('File upload error:', text);
                    throw new Error('Failed to upload file content');
                });
            }
            console.log('File uploaded successfully:', fileObj.name);
            return true;
        });
    }

    function readFileAsBase64(file) {
        return new Promise(function(resolve, reject) {
            var reader = new FileReader();
            reader.onload = function() {
                // Remove data URL prefix to get just base64
                var base64 = reader.result.split(',')[1];
                resolve(base64);
            };
            reader.onerror = function() {
                reject(new Error('Failed to read file'));
            };
            reader.readAsDataURL(file);
        });
    }

    function base64ToBlob(base64, mimeType) {
        var byteCharacters = atob(base64);
        var byteNumbers = new Array(byteCharacters.length);
        for (var i = 0; i < byteCharacters.length; i++) {
            byteNumbers[i] = byteCharacters.charCodeAt(i);
        }
        var byteArray = new Uint8Array(byteNumbers);
        return new Blob([byteArray], { type: mimeType });
    }

    function getRequestVerificationToken() {
        // Try multiple methods to get the token
        var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        if (tokenInput && tokenInput.value) {
            return tokenInput.value;
        }
        // Try getting from cookie
        var cookies = document.cookie.split(';');
        for (var i = 0; i < cookies.length; i++) {
            var cookie = cookies[i].trim();
            if (cookie.startsWith('__RequestVerificationToken=')) {
                return cookie.substring('__RequestVerificationToken='.length);
            }
        }
        // Try shell object (Power Pages specific)
        if (typeof shell !== 'undefined' && shell.getTokenDeferred) {
            return shell.getTokenDeferred();
        }
        return '';
    }

    function formatCurrency(value) {
        var num = parseFloat(value);
        if (isNaN(num)) return '0.00';
        return num.toFixed(2).replace(/\d(?=(\d{3})+\.)/g, '$&,');
    }

    function formatDate(dateStr) {
        if (!dateStr) return '-';
        var date = new Date(dateStr);
        return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
    }

    function escapeHtml(text) {
        if (!text) return '';
        var div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

})();
