function showEditReminderRecordModal(reminderId) {
    $.get(`/Vehicle/GetReminderRecordForEditById?reminderRecordId=${reminderId}`, function (data) {
        if (data) {
            $("#reminderRecordModalContent").html(data); 
            initDatePicker($('#reminderDate'), true);
            initTagSelector($("#reminderRecordTag"));
            $("#reminderRecordModal").modal("show");
            $('#reminderRecordModal').off('shown.bs.modal').on('shown.bs.modal', function () {
                if (getGlobalConfig().useMarkDown) {
                    toggleMarkDownOverlay("reminderNotes");
                }
            });
        }
    });
}
function hideAddReminderRecordModal() {
    $('#reminderRecordModal').modal('hide');
}
function checkCustomMonthInterval() {
    var selectedValue = $("#reminderRecurringMonth").val();
    if (selectedValue == "Other") {
        $("#workAroundInput").show();
        Swal.fire({
            title: 'Specify Custom Time Interval',
            html: `
                            <input type="text" inputmode="numeric" id="inputCustomMonth" class="swal2-input" placeholder="Time" onkeydown="handleSwalEnter(event)">
                            <select class="swal2-select" id="inputCustomMonthUnit">
                                <option value="Months">Months</option>
                                <option value="Days">Days</option>
                            </select>
                            `,
            confirmButtonText: 'Set',
            focusConfirm: false,
            preConfirm: () => {
                const customMonth = $("#inputCustomMonth").val();
                if (!customMonth || isNaN(parseInt(customMonth)) || parseInt(customMonth) <= 0) {
                    Swal.showValidationMessage(`Please enter a valid number`);
                }
                const customMonthUnit = $("#inputCustomMonthUnit").val();
                return { customMonth, customMonthUnit }
            },
        }).then(function (result) {
            if (result.isConfirmed) {
                customMonthInterval = result.value.customMonth;
                customMonthIntervalUnit = result.value.customMonthUnit;
                $("#reminderRecurringMonth > option[value='Other']").text(`Other: ${result.value.customMonth} ${result.value.customMonthUnit}`);
            } else {
                $("#reminderRecurringMonth").val(getReminderRecordModelData().monthInterval);
            }
            $("#workAroundInput").hide();
        });
    }
}
function checkCustomMileageInterval() {
    var selectedValue = $("#reminderRecurringMileage").val();
    if (selectedValue == "Other") {
        $("#workAroundInput").show();
        Swal.fire({
            title: 'Specify Custom Mileage Interval',
            html: `
                            <input type="text" inputmode="numeric" id="inputCustomMileage" class="swal2-input" placeholder="Mileage" onkeydown="handleSwalEnter(event)">
                            `,
            confirmButtonText: 'Set',
            focusConfirm: false,
            preConfirm: () => {
                const customMileage = $("#inputCustomMileage").val();
                if (!customMileage || isNaN(parseInt(customMileage)) || parseInt(customMileage) <= 0) {
                    Swal.showValidationMessage(`Please enter a valid number`);
                }
                return { customMileage }
            },
        }).then(function (result) {
            if (result.isConfirmed) {
                customMileageInterval = result.value.customMileage;
                $("#reminderRecurringMileage > option[value='Other']").text(`Other: ${result.value.customMileage} ${getReminderRecordModelData().distanceUnit}`);
            } else {
                $("#reminderRecurringMileage").val(getReminderRecordModelData().mileageInterval);
            }
            $("#workAroundInput").hide();
        });
    }
}
function deleteReminderRecord(reminderRecordId, e) {
    if (e != undefined) {
        event.stopPropagation();
    }
    $("#workAroundInput").show();
    confirmDelete("Deleted Reminders cannot be restored.", (result) => {
        if (result.isConfirmed) {
            $.post(`/Vehicle/DeleteReminderRecordById?reminderRecordId=${reminderRecordId}`, function (data) {
                if (data.success) {
                    hideAddReminderRecordModal();
                    successToast("Reminder Deleted");
                    var vehicleId = GetVehicleId().vehicleId;
                    getVehicleReminders(vehicleId);
                } else {
                    errorToast(data.message);
                    $("#workAroundInput").hide();
                }
            });
        } else {
            $("#workAroundInput").hide();
        }
    });
}
function toggleCustomThresholds() {
    var isChecked = $("#reminderUseCustomThresholds").is(':checked');
    if (isChecked) {
        $("#reminderCustomThresholds").collapse('show');
    } else {
        $("#reminderCustomThresholds").collapse('hide');
    }
}
function saveReminderRecordToVehicle(isEdit) {
    //get values
    var formValues = getAndValidateReminderRecordValues();
    //validate
    if (formValues.hasError) {
        errorToast("Please check the form data");
        return;
    }
    //save to db.
    $.post('/Vehicle/SaveReminderRecordToVehicleId', { reminderRecord: formValues }, function (data) {
        if (data.success) {
            successToast(isEdit ? "Reminder Updated" : "Reminder Added.");
            hideAddReminderRecordModal();
            if (!getReminderRecordModelData().createdFromRecord) {
                saveScrollPosition();
                getVehicleReminders(formValues.vehicleId);
            } else {
                getVehicleHaveImportantReminders(formValues.vehicleId);
            }
        } else {
            errorToast(data.message);
        }
    })
}
function appendMileageToOdometer(increment) {
    var reminderMileage = $("#reminderMileage").val();
    var reminderMileageIsInvalid = reminderMileage.trim() == '' || parseInt(reminderMileage) < 0;
    if (reminderMileageIsInvalid) {
        reminderMileage = 0;
    } else {
        reminderMileage = parseInt(reminderMileage);
    }
    reminderMileage += increment;
    $("#reminderMileage").val(reminderMileage);
}

function enableRecurring() {
    var reminderIsRecurring = $("#reminderIsRecurring").is(":checked");
    if (reminderIsRecurring) {
        $("#reminderFixedIntervals").attr('disabled', false);
        //check selected metric
        var reminderMetric = $('#reminderOptions input:radio:checked').val();
        if (reminderMetric == "Date") {
            $("#reminderRecurringMonth").attr('disabled', false);
            $("#reminderRecurringMileage").attr('disabled', true);
        }
        else if (reminderMetric == "Odometer") {
            $("#reminderRecurringMileage").attr('disabled', false);
            $("#reminderRecurringMonth").attr('disabled', true);
        }
        else if (reminderMetric == "Both") {
            $("#reminderRecurringMonth").attr('disabled', false);
            $("#reminderRecurringMileage").attr('disabled', false);
        }
    } else {
        $("#reminderRecurringMileage").attr('disabled', true);
        $("#reminderRecurringMonth").attr('disabled', true);
        $("#reminderFixedIntervals").attr('disabled', true);
    }
}

function markDoneReminderRecord(reminderRecordId, e) {
    event.stopPropagation();
    var vehicleId = GetVehicleId().vehicleId;
    $.post(`/Vehicle/PushbackRecurringReminderRecord?reminderRecordId=${reminderRecordId}`, function (data) {
        if (data.success) {
            successToast("Reminder Updated");
            getVehicleReminders(vehicleId);
        } else {
            errorToast(data.message);
        }
    });
}

function getAndValidateReminderRecordValues() {
    var reminderDate = $("#reminderDate").val();
    var reminderMileage = parseInt(globalParseFloat($("#reminderMileage").val())).toString();
    var reminderDescription = $("#reminderDescription").val();
    var reminderIsMaintenance = $("#reminderIsMaintenance").is(":checked");
    var reminderServiceKey = $("#reminderServiceKey").val() || "";
    var reminderNotes = $("#reminderNotes").val();
    var reminderOption = $('#reminderOptions input:radio:checked').val();
    var reminderIsRecurring = $("#reminderIsRecurring").is(":checked");
    var reminderRecurringMonth = $("#reminderRecurringMonth").val();
    var reminderRecurringMileage = $("#reminderRecurringMileage").val();
    var reminderTags = $("#reminderRecordTag").val();
    var vehicleId = GetVehicleId().vehicleId;
    var reminderId = getReminderRecordModelData().id;
    var reminderUseCustomThresholds = $("#reminderUseCustomThresholds").is(":checked");
    var reminderUrgentDays = $("#reminderUrgentDays").val();
    var reminderVeryUrgentDays = $("#reminderVeryUrgentDays").val();
    var reminderUrgentDistance = $("#reminderUrgentDistance").val();
    var reminderVeryUrgentDistance = $("#reminderVeryUrgentDistance").val();
    var reminderFixedIntervals = $("#reminderFixedIntervals").is(":checked");
    //validation
    var hasError = false;
    var reminderDateIsInvalid = reminderDate.trim() == ''; //eliminates whitespace.
    var reminderMileageIsInvalid = reminderMileage.trim() == '' || isNaN(reminderMileage) || parseInt(reminderMileage) < 0;
    if ((reminderOption == "Both" || reminderOption == "Date") && reminderDateIsInvalid) { 
        hasError = true;
        $("#reminderDate").addClass("is-invalid");
    } else if (reminderOption == "Date") {
        $("#reminderDate").removeClass("is-invalid");
    }
    if ((reminderOption == "Both" || reminderOption == "Odometer") && reminderMileageIsInvalid) {
        hasError = true;
        $("#reminderMileage").addClass("is-invalid");
    } else if (reminderOption == "Odometer") {
        $("#reminderMileage").removeClass("is-invalid");
    }
    if (reminderDescription.trim() == '') {
        hasError = true;
        $("#reminderDescription").addClass("is-invalid");
    } else {
        $("#reminderDescription").removeClass("is-invalid");
    }
    if (reminderIsMaintenance && !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(reminderServiceKey)) {
        hasError = true;
        $("#reminderServiceKey").addClass("is-invalid");
    } else {
        $("#reminderServiceKey").removeClass("is-invalid");
    }
    if (reminderUseCustomThresholds) {
        //validate custom threshold values
        if (reminderUrgentDays.trim() == '' || isNaN(reminderUrgentDays) || parseInt(reminderUrgentDays) < 0) {
            hasError = true;
            $("#reminderUrgentDays").addClass("is-invalid");
        } else {
            $("#reminderUrgentDays").removeClass("is-invalid");
        }
        if (reminderVeryUrgentDays.trim() == '' || isNaN(reminderVeryUrgentDays) || parseInt(reminderVeryUrgentDays) < 0) {
            hasError = true;
            $("#reminderVeryUrgentDays").addClass("is-invalid");
        } else {
            $("#reminderVeryUrgentDays").removeClass("is-invalid");
        }
        if (reminderUrgentDistance.trim() == '' || isNaN(reminderUrgentDistance) || parseInt(reminderUrgentDistance) < 0) {
            hasError = true;
            $("#reminderUrgentDistance").addClass("is-invalid");
        } else {
            $("#reminderUrgentDistance").removeClass("is-invalid");
        }
        if (reminderVeryUrgentDistance.trim() == '' || isNaN(reminderVeryUrgentDistance) || parseInt(reminderVeryUrgentDistance) < 0) {
            hasError = true;
            $("#reminderVeryUrgentDistance").addClass("is-invalid");
        } else {
            $("#reminderVeryUrgentDistance").removeClass("is-invalid");
        }
    }
    if (reminderOption == undefined) {
        hasError = true;
        $("#reminderMetricDate").addClass("is-invalid");
        $("#reminderMetricOdometer").addClass("is-invalid");
        $("#reminderMetricBoth").addClass("is-invalid");
    } else {
        $("#reminderMetricDate").removeClass("is-invalid");
        $("#reminderMetricOdometer").removeClass("is-invalid");
        $("#reminderMetricBoth").removeClass("is-invalid");
    }

    return {
        id: reminderId,
        hasError: hasError,
        vehicleId: vehicleId,
        date: reminderDate,
        mileage: reminderMileage,
        description: reminderDescription,
        isMaintenance: reminderIsMaintenance,
        serviceKey: reminderServiceKey,
        historyVerified: getReminderRecordModelData().historyVerified,
        notes: reminderNotes,
        metric: reminderOption,
        isRecurring: reminderIsRecurring,
        fixedIntervals: reminderFixedIntervals,
        useCustomThresholds: reminderUseCustomThresholds,
        customThresholds: {
            urgentDays: reminderUrgentDays,
            veryUrgentDays: reminderVeryUrgentDays,
            urgentDistance: reminderUrgentDistance,
            veryUrgentDistance: reminderVeryUrgentDistance
        },
        reminderMileageInterval: reminderRecurringMileage,
        reminderMonthInterval: reminderRecurringMonth,
        customMileageInterval: customMileageInterval,
        customMonthInterval: customMonthInterval,
        customMonthIntervalUnit: customMonthIntervalUnit,
        tags: reminderTags
    }
}

function toggleMaintenanceReminder() {
    var checked = $("#reminderIsMaintenance").is(":checked");
    $("#maintenanceServiceKeyContainer").toggle(checked);
    if (checked) {
        $("#reminderIsRecurring").prop("checked", true);
        $("#reminderFixedIntervals").prop("checked", false);
        enableRecurring();
        suggestMaintenanceServiceKey();
    }
}

function suggestMaintenanceServiceKey() {
    if (!$("#reminderIsMaintenance").is(":checked")) return;
    var input = $("#reminderServiceKey");
    if (input.val()) return;
    input.val($("#reminderDescription").val().toLowerCase().trim().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, ""));
}
function createPlanRecordFromReminder(reminderRecordId) {
    //get values
    var formValues = getAndValidateReminderRecordValues();
    //validate
    if (formValues.hasError) {
        errorToast("Please check the form data");
        return;
    }
    var planModelInput = {
        id: 0,
        createdFromReminder: true,
        vehicleId: formValues.vehicleId,
        reminderRecordId: reminderRecordId,
        description: formValues.description,
        notes: formValues.notes
    };
    $.post('/Vehicle/GetAddPlanRecordPartialView', { planModel: planModelInput }, function (data) {
        $("#reminderRecordModal").modal("hide");
        $("#planRecordModalContent").html(data);
        $("#planRecordModal").modal("show");
    });
}

function filterReminderTable(sender) {
    var rowData = $(`#reminder-tab-pane table tbody tr`);
    if (sender == undefined) {
        rowData.removeClass('override-hide');
        return;
    }
    var tagName = sender.textContent;
    //check for other applied filters
    if ($(sender).hasClass("bg-primary")) {
            rowData.removeClass('override-hide');
            $(sender).removeClass('bg-primary');
            $(sender).addClass('bg-secondary');
            updateReminderAggregateLabels();
    } else {
        //hide table rows.
        rowData.addClass('override-hide');
        $(`[data-tags~='${tagName}']`).removeClass('override-hide');
        updateReminderAggregateLabels();
        if ($(".tagfilter.bg-primary").length > 0) {
            //disabling other filters
            $(".tagfilter.bg-primary").addClass('bg-secondary');
            $(".tagfilter.bg-primary").removeClass('bg-primary');
        }
        $(sender).addClass('bg-primary');
        $(sender).removeClass('bg-secondary');
    }
}
function updateReminderAggregateLabels() {
    //update main count
    var newCount = $("[data-record-type='cost']").parent(":not('.override-hide')").length;
    var countLabel = $("[data-aggregate-type='count']");
    countLabel.text(`${countLabel.text().split(':')[0]}: ${newCount}`);
    //update labels
    //paste due
    var pastDueCount = $("tr td span.badge.text-bg-secondary").parents("tr:not('.override-hide')").length;
    var pastDueLabel = $('[data-aggregate-type="pastdue-count"]');
    pastDueLabel.text(`${pastDueLabel.text().split(':')[0]}: ${pastDueCount}`);
    //very urgent
    var veryUrgentCount = $("tr td span.badge.text-bg-danger").parents("tr:not('.override-hide')").length;
    var veryUrgentLabel = $('[data-aggregate-type="veryurgent-count"]');
    veryUrgentLabel.text(`${veryUrgentLabel.text().split(':')[0]}: ${veryUrgentCount}`);
    //urgent
    var urgentCount = $("tr td span.badge.text-bg-warning").parents("tr:not('.override-hide')").length;
    var urgentLabel = $('[data-aggregate-type="urgent-count"]');
    urgentLabel.text(`${urgentLabel.text().split(':')[0]}: ${urgentCount}`);
    //not urgent
    var notUrgentCount = $("tr td span.badge.text-bg-success").parents("tr:not('.override-hide')").length;
    var notUrgentLabel = $('[data-aggregate-type="noturgent-count"]');
    notUrgentLabel.text(`${notUrgentLabel.text().split(':')[0]}: ${notUrgentCount}`);
}

function filterMaintenanceReminders(mode, sender) {
    var rows = $("#reminder-tab-pane table tbody tr");
    rows.removeClass("override-hide");
    if (mode == "maintenance") rows.filter("[data-maintenance='false']").addClass("override-hide");
    if (mode == "other") rows.filter("[data-maintenance='true']").addClass("override-hide");
    $(".reminder-view-filter").removeClass("active");
    $(sender).addClass("active");
    var maintenanceColumns = $("#reminder-tab-pane [data-column='date'], #reminder-tab-pane [data-column='odometer'], #reminder-tab-pane [data-column='duedays'], #reminder-tab-pane [data-column='duedistance'], #reminder-tab-pane [data-column='lastservice']");
    if (mode == "maintenance") {
        maintenanceColumns.show();
        $("#reminder-tab-pane [data-column='metric'], #reminder-tab-pane [data-column='notes']").hide();
    } else {
        maintenanceColumns.hide();
        $("#reminder-tab-pane [data-column='metric'], #reminder-tab-pane [data-column='notes']").show();
    }
    updateReminderAggregateLabels();
}

function showMaintenanceImportModal() {
    $.get(`/Vehicle/GetMaintenanceImportPartialView?vehicleId=${GetVehicleId().vehicleId}`, function (data) {
        $("#bulkImportModalContent").html(data);
        $("#bulkImportModal").modal("show");
    });
}

function clearMaintenanceImportPreview() {
    $("#maintenanceImportResults").empty();
    $("#commitMaintenanceImportButton").prop("disabled", true);
}

function readMaintenanceImportFile(input) {
    clearMaintenanceImportPreview();
    var file = input.files[0];
    if (!file) return;
    if (file.size > 1024 * 1024) {
        errorToast("Import exceeds the 1 MB limit");
        input.value = "";
        return;
    }
    var reader = new FileReader();
    reader.onload = event => $("#maintenanceImportContent").val(event.target.result);
    reader.readAsText(file);
}

function maintenanceImportRequest() {
    return {
        vehicleId: window.maintenanceImportVehicleId,
        type: $("#maintenanceImportType").val(),
        content: $("#maintenanceImportContent").val()
    };
}

function previewMaintenanceImport() {
    $.post("/Vehicle/PreviewMaintenanceImport", { request: maintenanceImportRequest() }, function (data) {
        renderMaintenanceImportResults(data);
        $("#commitMaintenanceImportButton").prop("disabled", !data.success);
    });
}

function commitMaintenanceImport() {
    $("#commitMaintenanceImportButton").prop("disabled", true);
    $.post("/Vehicle/CommitMaintenanceImport", { request: maintenanceImportRequest() }, function (data) {
        renderMaintenanceImportResults(data);
        if (data.success) {
            successToast("Maintenance data imported");
            getVehicleReminders(GetVehicleId().vehicleId);
        }
    });
}

function renderMaintenanceImportResults(data) {
    var rows = data.rows || [];
    var html = `<div class="table-responsive"><table class="table table-sm"><thead><tr><th>Row</th><th>Service key</th><th>Status</th><th>Details</th></tr></thead><tbody>`;
    rows.forEach(row => {
        var color = row.isError ? "danger" : row.status == "Warning" ? "warning" : row.status == "Skip" ? "secondary" : "success";
        html += `<tr><td>${row.row}</td><td>${encodeHTMLInput(row.serviceKey)}</td><td><span class="badge text-bg-${color}">${encodeHTMLInput(row.status)}</span></td><td>${encodeHTMLInput(row.message)}</td></tr>`;
    });
    html += "</tbody></table></div>";
    $("#maintenanceImportResults").html(html);
}

function downloadMaintenanceTemplate() {
    window.location.href = `/Vehicle/DownloadMaintenanceTemplate?vehicleId=${window.maintenanceImportVehicleId}&type=${$("#maintenanceImportType").val()}`;
}

function copyMaintenanceAiPrompt() {
    var schedule = $("#maintenanceImportType").val() == "Schedule";
    var fields = schedule
        ? "service_key,name,mileage_interval,month_interval,notes"
        : "service_key,date,odometer,cost,notes";
    var prompt = `For ${document.title}, return only ${schedule ? "the recommended recurring maintenance schedule" : "the maintenance events in the attached report"} as CSV. Use exactly these headers: ${fields}. Use stable lowercase hyphenated service_key values, ISO YYYY-MM-DD dates, plain numbers without units or currency symbols, and leave optional unknown values blank.`;
    navigator.clipboard.writeText(prompt).then(() => successToast("AI instructions copied"));
}
