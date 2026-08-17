(function () {
    'use strict';

    var pluginId = '41341b7d-9374-4c82-824a-21d360036771';
    var page = document.querySelector('#AttachmentOptimizerConfigPage');
    var form = page.querySelector('#AttachmentOptimizerConfigForm');

    function getChecked(containerId) {
        return Array.from(page.querySelectorAll('#' + containerId + ' input:checked'))
            .map(function (input) { return input.value; });
    }

    function updateChoiceCount(containerId, counterId, emptyLabel, fullLabel) {
        var inputs = Array.from(page.querySelectorAll('#' + containerId + ' input'));
        var selectedCount = inputs.filter(function (input) { return input.checked; }).length;
        var counter = page.querySelector('#' + counterId);

        if (selectedCount === 0) {
            counter.textContent = emptyLabel;
        } else if (fullLabel && selectedCount === inputs.length) {
            counter.textContent = fullLabel;
        } else {
            counter.textContent = selectedCount + ' selected';
        }
    }

    function createCheckboxList(items, selectedValues, containerId, counterId, emptyLabel, fullLabel) {
        var container = page.querySelector('#' + containerId);
        var selected = new Set(selectedValues || []);
        container.textContent = '';

        items.forEach(function (item) {
            var label = document.createElement('label');
            label.className = 'emby-checkbox-label';
            label.innerHTML = '<input type="checkbox" is="emby-checkbox"><span></span>';

            var input = label.querySelector('input');
            input.value = item.Value;
            input.checked = selected.has(item.Value);
            input.addEventListener('change', function () {
                updateChoiceCount(containerId, counterId, emptyLabel, fullLabel);
            });

            label.querySelector('span').textContent = item.Text;
            container.appendChild(label);
        });

        updateChoiceCount(containerId, counterId, emptyLabel, fullLabel);
    }

    async function populateLibraries(config) {
        var folders = await ApiClient.getVirtualFolders();
        var libraries = folders
            .filter(function (folder) {
                return folder.CollectionType === undefined
                    || folder.CollectionType === 'tvshows'
                    || folder.CollectionType === 'movies';
            })
            .map(function (folder) {
                return { Value: folder.Name, Text: folder.Name };
            });

        createCheckboxList(
            libraries,
            config.SelectedSubtitleLibraries,
            'SubtitleLibraryList',
            'SubtitleLibraryCount',
            'All libraries');
        createCheckboxList(
            libraries,
            config.SelectedAttachmentLibraries,
            'AttachmentLibraryList',
            'AttachmentLibraryCount',
            'All libraries');
    }

    function updateSubtitleFormatLimit() {
        var limited = page.querySelector('#LimitSubtitleFormats').checked;
        page.querySelector('#SubtitleCodecMode').style.display = limited ? '' : 'none';
    }

    page.addEventListener('pageshow', function () {
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(pluginId)
            .then(async function (config) {
                page.querySelector('#ExtractSubtitlesDuringLibraryScan').checked =
                    !!config.ExtractSubtitlesDuringLibraryScan;
                page.querySelector('#PrecacheAttachmentsDuringLibraryScan').checked =
                    !!config.PrecacheAttachmentsDuringLibraryScan;
                page.querySelector('#LimitSubtitleFormats').checked =
                    !!config.EnableAdvancedSubtitleCodecSelection;
                page.querySelector('#EnableBatchExtraction').checked =
                    config.EnableBatchExtraction !== false;
                page.querySelector('#EnableDeduplication').checked =
                    config.EnableDeduplication !== false;
                page.querySelector('#EnableHardLinks').checked =
                    config.EnableHardLinks !== false;
                page.querySelector('#EnableAutomaticCleanup').checked =
                    !!config.EnableAutomaticCleanup;
                page.querySelector('#CleanupDryRun').checked =
                    config.CleanupDryRun !== false;
                page.querySelector('#CompatibilityFileRetentionHours').value =
                    config.CompatibilityFileRetentionHours;
                page.querySelector('#BlobRetentionDays').value = config.BlobRetentionDays;
                page.querySelector('#MaximumBlobCacheSizeGiB').value =
                    config.MaximumBlobCacheSizeGiB;

                createCheckboxList(
                    config.AllSubtitleCodecs || [],
                    config.SelectedSubtitleCodecs || [],
                    'SubtitleCodecList',
                    'SubtitleCodecCount',
                    'None selected',
                    'All selected');
                await populateLibraries(config);
                updateSubtitleFormatLimit();
            })
            .catch(function (error) {
                console.error('Unable to load Attachment Optimizer settings', error);
            })
            .finally(function () {
                Dashboard.hideLoadingMsg();
            });
    });

    page.querySelector('#LimitSubtitleFormats')
        .addEventListener('change', updateSubtitleFormatLimit);

    form.addEventListener('submit', function (event) {
        event.preventDefault();
        Dashboard.showLoadingMsg();

        ApiClient.getPluginConfiguration(pluginId)
            .then(function (config) {
                config.ExtractSubtitlesDuringLibraryScan =
                    page.querySelector('#ExtractSubtitlesDuringLibraryScan').checked;
                config.PrecacheAttachmentsDuringLibraryScan =
                    page.querySelector('#PrecacheAttachmentsDuringLibraryScan').checked;
                config.SelectedSubtitleLibraries = getChecked('SubtitleLibraryList');
                config.SelectedAttachmentLibraries = getChecked('AttachmentLibraryList');
                config.EnableAdvancedSubtitleCodecSelection =
                    page.querySelector('#LimitSubtitleFormats').checked;
                config.SelectedSubtitleCodecs = getChecked('SubtitleCodecList');
                config.EnableBatchExtraction =
                    page.querySelector('#EnableBatchExtraction').checked;
                config.EnableDeduplication =
                    page.querySelector('#EnableDeduplication').checked;
                config.EnableHardLinks = page.querySelector('#EnableHardLinks').checked;
                config.EnableAutomaticCleanup =
                    page.querySelector('#EnableAutomaticCleanup').checked;
                config.CleanupDryRun = page.querySelector('#CleanupDryRun').checked;
                config.CompatibilityFileRetentionHours =
                    Number(page.querySelector('#CompatibilityFileRetentionHours').value);
                config.BlobRetentionDays =
                    Number(page.querySelector('#BlobRetentionDays').value);
                config.MaximumBlobCacheSizeGiB =
                    Number(page.querySelector('#MaximumBlobCacheSizeGiB').value);

                return ApiClient.updatePluginConfiguration(pluginId, config);
            })
            .then(Dashboard.processPluginConfigurationUpdateResult)
            .catch(function (error) {
                console.error('Unable to save Attachment Optimizer settings', error);
                Dashboard.hideLoadingMsg();
            });

        return false;
    });
})();
