// Profile Page Redesign - Apex Claims
(function() {
    'use strict';

    function init() {
        hideSidebar();
        hidePageTitle();
        addProfileHeader();
        hideUnwantedFields();
        wrapFormInCard();
        wrapSecurityInCard();
    }

    function hideSidebar() {
        // Hide all sidebar elements via JS (belt and suspenders with CSS)
        var sidebarSelectors = [
            '.profile-sidebar',
            '.sidebar',
            '.nav-profile',
            '.profile-navigation',
            '.nav-sidebar',
            'aside',
            '.side-nav',
            '.list-group'
        ];

        sidebarSelectors.forEach(function(selector) {
            var elements = document.querySelectorAll(selector);
            elements.forEach(function(el) {
                // Check if this is actually a sidebar (contains profile link or avatar)
                if (el.querySelector('a[href*="profile"]') ||
                    el.querySelector('.nav-profile') ||
                    el.closest('aside') ||
                    el.classList.contains('nav-profile')) {
                    el.style.display = 'none';
                }
            });
        });

        // Also hide parent columns that contained the sidebar
        var asideElements = document.querySelectorAll('aside, .col-md-3, .col-lg-3, .col-md-4, .col-lg-4');
        asideElements.forEach(function(el) {
            if (el.querySelector('.list-group') || el.querySelector('.nav-profile') || el.tagName === 'ASIDE') {
                el.style.display = 'none';
            }
        });
    }

    function hidePageTitle() {
        // Hide the duplicate "Profile" page title
        var pageHeaders = document.querySelectorAll('.page-heading, h1.page-header, .breadcrumb + h1');
        pageHeaders.forEach(function(header) {
            if (header.textContent.trim().toLowerCase() === 'profile') {
                header.style.display = 'none';
            }
        });
    }

    function addProfileHeader() {
        // Get user info from the page
        var firstNameInput = document.getElementById('firstname');
        var lastNameInput = document.getElementById('lastname');
        var emailInput = document.getElementById('emailaddress1');

        var firstName = firstNameInput ? firstNameInput.value : '';
        var lastName = lastNameInput ? lastNameInput.value : '';
        var email = emailInput ? emailInput.value : '';
        var fullName = [firstName, lastName].filter(Boolean).join(' ') || 'User';

        // Generate initials
        var initials = '';
        if (firstName) initials += firstName.charAt(0).toUpperCase();
        if (lastName) initials += lastName.charAt(0).toUpperCase();
        if (!initials) initials = 'U';

        // Create profile header
        var headerHtml = '<div class="profile-header">' +
            '<div class="profile-avatar">' + initials + '</div>' +
            '<div class="profile-info">' +
            '<h1>' + escapeHtml(fullName) + '</h1>' +
            '<p>' + escapeHtml(email) + '</p>' +
            '</div>' +
            '</div>';

        // Find the main content area and insert header
        var pageContent = document.querySelector('.page-copy');
        if (pageContent) {
            pageContent.insertAdjacentHTML('afterbegin', headerHtml);
        }

        // Update header when name fields change
        if (firstNameInput) {
            firstNameInput.addEventListener('change', updateProfileHeader);
        }
        if (lastNameInput) {
            lastNameInput.addEventListener('change', updateProfileHeader);
        }
    }

    function updateProfileHeader() {
        var firstNameInput = document.getElementById('firstname');
        var lastNameInput = document.getElementById('lastname');
        var emailInput = document.getElementById('emailaddress1');

        var firstName = firstNameInput ? firstNameInput.value : '';
        var lastName = lastNameInput ? lastNameInput.value : '';
        var email = emailInput ? emailInput.value : '';
        var fullName = [firstName, lastName].filter(Boolean).join(' ') || 'User';

        // Update initials
        var initials = '';
        if (firstName) initials += firstName.charAt(0).toUpperCase();
        if (lastName) initials += lastName.charAt(0).toUpperCase();
        if (!initials) initials = 'U';

        var avatarEl = document.querySelector('.profile-avatar');
        var nameEl = document.querySelector('.profile-info h1');

        if (avatarEl) avatarEl.textContent = initials;
        if (nameEl) nameEl.textContent = fullName;
    }

    function hideUnwantedFields() {
        // List of field IDs to hide
        var fieldsToHide = [
            'parentcustomerid',
            'parentcustomerid_name',
            'parentcustomerid_entityname',
            'jobtitle',
            'nickname',
            'websiteurl',
            'adx_publicprofilecopy',
            'preferredcontactmethodcode',
            'adx_preferredlanguageid',
            'fax'
        ];

        fieldsToHide.forEach(function(fieldId) {
            var field = document.getElementById(fieldId);
            if (field) {
                // Hide the field's parent row/cell
                var row = field.closest('tr') || field.closest('.cell') || field.closest('.form-group');
                if (row) {
                    row.style.display = 'none';
                }
            }
        });

        // Hide sections by label text
        var labelsToHide = [
            'Organization Name',
            'Title',
            'Nickname',
            'Website',
            'Public Profile Copy',
            'Preferred Language',
            'How may we contact you',
            'Fax'
        ];

        var allLabels = document.querySelectorAll('label, legend, th');
        allLabels.forEach(function(label) {
            var labelText = label.textContent.trim();
            labelsToHide.forEach(function(hideText) {
                if (labelText.toLowerCase().includes(hideText.toLowerCase())) {
                    var container = label.closest('tr') || label.closest('.cell') ||
                                   label.closest('fieldset') || label.closest('.form-group');
                    if (container) {
                        container.style.display = 'none';
                    }
                }
            });
        });

        // Hide contact method checkboxes (Email, Fax, Phone, Mail)
        var checkboxes = document.querySelectorAll('input[type="checkbox"]');
        checkboxes.forEach(function(checkbox) {
            var label = checkbox.parentElement;
            if (label) {
                var text = label.textContent.toLowerCase();
                if (text.includes('email') || text.includes('fax') ||
                    text.includes('phone') || text.includes('mail')) {
                    var container = checkbox.closest('fieldset') || checkbox.closest('.form-group') ||
                                   checkbox.closest('tr') || label;
                    if (container && container.tagName === 'FIELDSET') {
                        container.style.display = 'none';
                    }
                }
            }
        });
    }

    function wrapFormInCard() {
        // Find the form and wrap it in a card
        var form = document.querySelector('.crmEntityFormView') || document.querySelector('form[action*="profile"]');
        if (form && !form.closest('.profile-card')) {
            var card = document.createElement('div');
            card.className = 'profile-card';

            var cardTitle = document.createElement('h2');
            cardTitle.textContent = 'Personal Information';

            form.parentNode.insertBefore(card, form);
            card.appendChild(cardTitle);
            card.appendChild(form);
        }
    }

    function escapeHtml(text) {
        if (!text) return '';
        var div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    function wrapSecurityInCard() {
        // Find security-related links
        var securityLinks = [];
        var linkSelectors = [
            'a[href*="set-password"]',
            'a[href*="change-email"]',
            'a[href*="manage-external-authentication"]',
            'a[href*="authentication"]'
        ];

        linkSelectors.forEach(function(selector) {
            var links = document.querySelectorAll(selector);
            links.forEach(function(link) {
                if (!securityLinks.includes(link)) {
                    securityLinks.push(link);
                }
            });
        });

        // If no security links found, try to find by text content
        if (securityLinks.length === 0) {
            var allLinks = document.querySelectorAll('a');
            allLinks.forEach(function(link) {
                var text = link.textContent.toLowerCase();
                if (text.includes('password') || text.includes('email') || text.includes('authentication')) {
                    securityLinks.push(link);
                }
            });
        }

        if (securityLinks.length === 0) return;

        // Check if already wrapped
        if (document.querySelector('.security-card')) return;

        // Find the common parent or the area where security links are
        var firstLink = securityLinks[0];
        var securityContainer = firstLink.closest('fieldset') ||
                                firstLink.closest('.section') ||
                                firstLink.closest('div');

        // Create security card
        var card = document.createElement('div');
        card.className = 'security-card';

        var cardTitle = document.createElement('h2');
        cardTitle.textContent = 'Security';

        var list = document.createElement('ul');
        list.className = 'security-list';

        // Clone and add links to list
        securityLinks.forEach(function(link) {
            var li = document.createElement('li');
            li.className = 'security-item';

            var clonedLink = link.cloneNode(true);
            li.appendChild(clonedLink);
            list.appendChild(li);

            // Hide original link
            var originalContainer = link.closest('fieldset') || link.closest('.form-group') || link.parentElement;
            if (originalContainer) {
                originalContainer.style.display = 'none';
            }
        });

        card.appendChild(cardTitle);
        card.appendChild(list);

        // Insert after the profile card
        var profileCard = document.querySelector('.profile-card');
        if (profileCard) {
            profileCard.parentNode.insertBefore(card, profileCard.nextSibling);
        } else {
            // Fallback: insert at end of page-copy
            var pageCopy = document.querySelector('.page-copy');
            if (pageCopy) {
                pageCopy.appendChild(card);
            }
        }
    }

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
