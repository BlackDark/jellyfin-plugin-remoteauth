var pluginId = 'a3c7e891-f2b4-4d6a-9e58-1b2c3d4e5f60';
var cfg = null;
var libs = {};

function esc(str) {
    var d = document.createElement('div');
    d.textContent = str;
    return d.innerHTML;
}

function gval(view, id) {
    var el = view.querySelector('#' + id);
    return el ? el.value : '';
}

function gchk(view, id) {
    var el = view.querySelector('#' + id);
    return el ? el.checked : false;
}

function fld(label, type, id, value, placeholder, full) {
    return '<div class="ra-field' + (full ? ' full' : '') + '">' +
        '<label for="' + id + '">' + esc(label) + '</label>' +
        '<input type="' + type + '" id="' + id + '" value="' + esc(String(value || '')) + '"' +
        (placeholder ? ' placeholder="' + esc(placeholder) + '"' : '') + ' />' +
        '</div>';
}

function chk(id, label, checked) {
    return '<label><input type="checkbox" id="' + id + '"' + (checked ? ' checked' : '') + ' /> ' + esc(label) + '</label>';
}

function addLibChip(container, libId) {
    var chip = document.createElement('span');
    chip.className = 'ra-library-chip';
    chip.setAttribute('data-lib-id', libId);
    chip.innerHTML = esc(libs[libId] || libId) + ' <span class="remove">&times;</span>';
    container.appendChild(chip);
}

function renderRoleMappings(view) {
    var container = view.querySelector('#roleMappingList');
    container.innerHTML = '';
    cfg.RoleMappings.forEach(function (m, idx) {
        var card = document.createElement('div');
        card.className = 'ra-card';
        var libOpts = Object.keys(libs).map(function (id) {
            return '<option value="' + esc(id) + '">' + esc(libs[id]) + '</option>';
        }).join('');
        var selectedLibs = (m.LibraryIds || []).concat(
            (m.LibraryNames || []).map(function (name) {
                var f = Object.keys(libs).find(function (id) {
                    return libs[id].toLowerCase() === name.toLowerCase();
                });
                return f || name;
            })
        );
        card.innerHTML = '<h4>Role: ' + esc(m.RoleName || 'New Role') + '</h4>' +
            '<div class="ra-grid">' +
            fld('Role / Group Name', 'text', 'role_name_' + idx, m.RoleName, 'Must match group value from IdP') +
            fld('Priority', 'number', 'role_priority_' + idx, m.Priority || 0, 'Higher = takes precedence') +
            '</div>' +
            '<div class="ra-checkbox-row">' +
            chk('role_admin_' + idx, 'Administrator', m.IsAdmin) +
            chk('role_alllibs_' + idx, 'All Libraries', m.EnableAllLibraries) +
            chk('role_livetv_' + idx, 'Live TV', m.EnableLiveTv) +
            chk('role_livetvmgmt_' + idx, 'Live TV Mgmt', m.EnableLiveTvManagement) +
            chk('role_playback_' + idx, 'Playback', m.EnableMediaPlayback !== false) +
            chk('role_remote_' + idx, 'Remote Access', m.EnableRemoteAccess !== false) +
            chk('role_transcode_' + idx, 'Transcoding', m.EnableTranscoding !== false) +
            chk('role_delete_' + idx, 'Delete Content', m.EnableContentDeletion) +
            chk('role_collections_' + idx, 'Collections', m.EnableCollectionManagement) +
            chk('role_subtitles_' + idx, 'Subtitles', m.EnableSubtitleManagement) +
            '</div>' +
            '<div class="ra-field" style="margin-top:0.5em;">' +
            '<label>Libraries (when "All Libraries" is unchecked)</label>' +
            '<select id="role_libadd_' + idx + '"><option value="">-- Select library --</option>' + libOpts + '</select>' +
            '<button type="button" class="ra-btn-secondary" style="margin-top:0.3em;width:fit-content;" data-action="add-lib" data-idx="' + idx + '">Add Library</button>' +
            '<div id="role_libs_' + idx + '" class="ra-library-list"></div>' +
            '</div>' +
            '<div class="ra-field" style="margin-top:0.5em;">' +
            '<label>Max Parental Rating (empty = unrestricted)</label>' +
            '<input type="number" id="role_maxrating_' + idx + '" value="' + (m.MaxParentalRating != null ? m.MaxParentalRating : '') + '" />' +
            '</div>' +
            '<div style="margin-top:0.5em;">' +
            '<button type="button" class="ra-btn-remove" data-action="remove-role" data-idx="' + idx + '">Remove</button>' +
            '</div>';
        container.appendChild(card);
        var libCont = view.querySelector('#role_libs_' + idx);
        selectedLibs.forEach(function (libId) { addLibChip(libCont, libId); });
    });
}

function collectRoleMappings(view) {
    var result = [];
    view.querySelectorAll('#roleMappingList .ra-card').forEach(function (card, idx) {
        var chips = view.querySelectorAll('#role_libs_' + idx + ' .ra-library-chip');
        var libIds = [];
        chips.forEach(function (c) { libIds.push(c.getAttribute('data-lib-id')); });
        var mr = gval(view, 'role_maxrating_' + idx);
        result.push({
            RoleName: gval(view, 'role_name_' + idx),
            Priority: parseInt(gval(view, 'role_priority_' + idx)) || 0,
            IsAdmin: gchk(view, 'role_admin_' + idx),
            EnableAllLibraries: gchk(view, 'role_alllibs_' + idx),
            LibraryIds: libIds, LibraryNames: [],
            EnableLiveTv: gchk(view, 'role_livetv_' + idx),
            EnableLiveTvManagement: gchk(view, 'role_livetvmgmt_' + idx),
            EnableMediaPlayback: gchk(view, 'role_playback_' + idx),
            EnableRemoteAccess: gchk(view, 'role_remote_' + idx),
            EnableTranscoding: gchk(view, 'role_transcode_' + idx),
            EnableContentDeletion: gchk(view, 'role_delete_' + idx),
            EnableCollectionManagement: gchk(view, 'role_collections_' + idx),
            EnableSubtitleManagement: gchk(view, 'role_subtitles_' + idx),
            MaxParentalRating: mr ? parseInt(mr) : null
        });
    });
    return result;
}

function loadStatus(view) {
    ApiClient.getJSON(ApiClient.getUrl('sso/RemoteAuth/Config/Status')).then(function (s) {
        var el = view.querySelector('#statusContent');
        var color = (s.Enabled && s.Configured) ? '#4caf50' : '#f44336';
        var state = (s.Enabled && s.Configured) ? 'Active' : (!s.Enabled ? 'Disabled' : 'Not configured (secret missing)');
        el.innerHTML =
            '<p><strong>Status:</strong> <span style="color:' + color + '">' + esc(state) + '</span></p>' +
            '<p><strong>Plugin version:</strong> ' + esc(s.PluginVersion) + '</p>' +
            '<p><strong>Role mappings:</strong> ' + s.RoleMappingCount + '</p>' +
            '<p><strong>Auto-create users:</strong> ' + (s.AutoCreateUsers ? 'Yes' : 'No') + '</p>' +
            '<p><strong>Default role:</strong> ' + esc(s.DefaultRoleName || '(none)') + '</p>';
    }).catch(function () {
        var el = view.querySelector('#statusContent');
        el.innerHTML = '<p style="color:#f44336">Failed to load status</p>';
    });
}

export default function (view) {
    view.addEventListener('viewshow', function () {
        Dashboard.showLoadingMsg();

        ApiClient.getJSON(ApiClient.getUrl('sso/RemoteAuth/Config/Libraries')).then(function (data) {
            libs = data || {};
        }).catch(function () {
            libs = {};
        }).then(function () {
            return ApiClient.getPluginConfiguration(pluginId);
        }).then(function (config) {
            cfg = config;
            cfg.RoleMappings = cfg.RoleMappings || [];

            // General tab
            view.querySelector('#pluginEnabled').checked = cfg.Enabled !== false;
            view.querySelector('#secretHeaderName').value = cfg.SecretHeaderName || 'X-Remote-Auth-Secret';
            view.querySelector('#secretHeaderValue').value = cfg.SecretHeaderValue || '';
            view.querySelector('#userHeader').value = cfg.UserHeader || 'X-Remote-Auth-User';
            view.querySelector('#emailHeader').value = cfg.EmailHeader || 'X-Remote-Auth-Email';
            view.querySelector('#displayNameHeader').value = cfg.DisplayNameHeader || 'X-Remote-Auth-Name';
            view.querySelector('#groupsHeader').value = cfg.GroupsHeader || 'X-Remote-Auth-Groups';
            view.querySelector('#groupsDelimiter').value = cfg.GroupsDelimiter || '|';
            view.querySelector('#adminGroup').value = cfg.AdminGroup || '';
            view.querySelector('#autoCreateUsers').checked = cfg.AutoCreateUsers !== false;
            view.querySelector('#defaultRoleName').value = cfg.DefaultRoleName || '';

            renderRoleMappings(view);
            Dashboard.hideLoadingMsg();
        }).catch(function (err) {
            Dashboard.hideLoadingMsg();
            console.error('RemoteAuth: failed to load config', err);
        });
    });

    // Tabs
    view.querySelectorAll('.ra-tab').forEach(function (tab) {
        tab.addEventListener('click', function () {
            view.querySelectorAll('.ra-tab').forEach(function (t) {
                t.style.borderBottomColor = 'transparent';
                t.style.color = '#aaa';
            });
            view.querySelectorAll('.ra-tab-content').forEach(function (c) {
                c.style.display = 'none';
            });
            this.style.borderBottomColor = '#00a4dc';
            this.style.color = '#00a4dc';
            var tabName = this.getAttribute('data-tab');
            view.querySelector('#tab-' + tabName).style.display = 'block';
            if (tabName === 'status') {
                loadStatus(view);
            }
        });
    });

    // Add role mapping
    view.querySelector('#btnAddRoleMapping').addEventListener('click', function () {
        if (!cfg) return;
        cfg.RoleMappings.push({
            RoleName: '', Priority: 0, IsAdmin: false, EnableAllLibraries: false,
            LibraryIds: [], LibraryNames: [], EnableLiveTv: false,
            EnableLiveTvManagement: false, EnableMediaPlayback: true,
            EnableRemoteAccess: true, EnableTranscoding: true,
            EnableContentDeletion: false, EnableCollectionManagement: false,
            EnableSubtitleManagement: false, MaxParentalRating: null
        });
        renderRoleMappings(view);
    });

    // Save
    view.querySelector('#btnSave').addEventListener('click', function () {
        if (!cfg) return;
        Dashboard.showLoadingMsg();
        cfg.Enabled = gchk(view, 'pluginEnabled');
        cfg.SecretHeaderName = gval(view, 'secretHeaderName');
        cfg.SecretHeaderValue = gval(view, 'secretHeaderValue');
        cfg.UserHeader = gval(view, 'userHeader');
        cfg.EmailHeader = gval(view, 'emailHeader');
        cfg.DisplayNameHeader = gval(view, 'displayNameHeader');
        cfg.GroupsHeader = gval(view, 'groupsHeader');
        cfg.GroupsDelimiter = gval(view, 'groupsDelimiter');
        cfg.AdminGroup = gval(view, 'adminGroup');
        cfg.AutoCreateUsers = gchk(view, 'autoCreateUsers');
        cfg.DefaultRoleName = gval(view, 'defaultRoleName');
        cfg.RoleMappings = collectRoleMappings(view);
        ApiClient.updatePluginConfiguration(pluginId, cfg).then(function (result) {
            Dashboard.processPluginConfigurationUpdateResult(result);
            Dashboard.hideLoadingMsg();
        }).catch(function (err) {
            Dashboard.hideLoadingMsg();
            Dashboard.alert('Failed to save: ' + (err.message || err));
        });
    });

    // Event delegation for role mapping list
    view.querySelector('#roleMappingList').addEventListener('click', function (e) {
        if (e.target.classList.contains('remove')) {
            e.target.parentElement.remove();
            return;
        }
        var btn = e.target.closest('[data-action]');
        if (!btn) return;
        var idx = parseInt(btn.getAttribute('data-idx'));
        if (btn.getAttribute('data-action') === 'remove-role') {
            cfg.RoleMappings.splice(idx, 1);
            renderRoleMappings(view);
        } else if (btn.getAttribute('data-action') === 'add-lib') {
            var sel = view.querySelector('#role_libadd_' + idx);
            if (!sel || !sel.value) return;
            var cont = view.querySelector('#role_libs_' + idx);
            var chips = cont.querySelectorAll('.ra-library-chip');
            for (var i = 0; i < chips.length; i++) {
                if (chips[i].getAttribute('data-lib-id') === sel.value) return;
            }
            addLibChip(cont, sel.value);
            sel.value = '';
        }
    });
}
