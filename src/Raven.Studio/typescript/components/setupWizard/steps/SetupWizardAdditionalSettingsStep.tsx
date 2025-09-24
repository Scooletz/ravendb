import { Control, useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import { Icon } from "components/common/Icon";
import Button from "react-bootstrap/Button";
import { FormGroup, FormInput, FormLabel, FormPathSelector, FormSelect, FormSwitch } from "components/common/Form";
import { setupWizardConstants, setupWizardGA4Prefixes } from "components/setupWizard/utils/setupWizardConstants";
import { HrHeader } from "components/common/HrHeader";
import classNames from "classnames";
import { useEffect } from "react";
import { getLicenseType } from "components/setupWizard/utils/setupWizardUtils";
import PopoverWithHoverWrapper from "components/common/PopoverWithHoverWrapper";
import { PopoverMessage } from "components/setupWizard/steps/SetupWizardNodeAddressStep";
import { ConditionalPopover } from "components/common/ConditionalPopover";
import { useServices } from "hooks/useServices";
import { useEventsCollector } from "components/hooks/useEventsCollector";
import { OperatingSystem, useOS } from "hooks/useOS";

export function SetupWizardAdditionalSettingsStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { additionalSettingsStep, licenseKeyStep } = useWatch({ control });

    const { licenseInfo } = licenseKeyStep;

    AdditionalSettingsFormSideEffects();

    return (
        <div className="setup-wizard-additional-settings">
            {getLicenseType(licenseInfo).isHigherThan("None") && (
                <>
                    <div className="mb-4">
                        <h2 className="mb-1">Additional settings</h2>
                        <p className="mb-4 text-muted">
                            At this optional step you may control some of optional settings regarding your setup.
                        </p>
                    </div>
                    <div>
                        <ServerEnvironmentSection control={control} licenseInfo={licenseInfo} />
                        <CertificateExpirationSection control={control} />
                        <HrHeader />
                    </div>
                </>
            )}
            <ExperimentalFeaturesSection control={control} licenseInfo={licenseInfo} />
            <div className="d-flex gap-2 align-items-center">
                <h2 className="mb-1">Configure advanced options</h2>
                <FormSwitch size="lg" name="additionalSettingsStep.isAdvancedSettingsVisible" control={control} />
            </div>
            <p className="text-muted">Settings you may want to consider as an experienced user</p>
            <AdvancedSettingsContent control={control} isVisible={additionalSettingsStep.isAdvancedSettingsVisible} />
        </div>
    );
}

function AdditionalSettingsFormSideEffects() {
    const { reportEvent } = useEventsCollector();
    const { setValue, watch, control } = useFormContext<SetupWizardFormData>();

    const { licenseKeyStep } = useWatch({ control });

    useEffect(() => {
        if (getLicenseType(licenseKeyStep.licenseInfo).isDeveloper()) {
            setValue("additionalSettingsStep.serverEnvironment", "Development", {
                shouldDirty: true,
            });
        } else if (getLicenseType(licenseKeyStep.licenseInfo).isProfessionalOrHigher()) {
            setValue("additionalSettingsStep.serverEnvironment", "Production", {
                shouldDirty: true,
            });
        }

        const { unsubscribe } = watch((values, { name }) => {
            if (
                name === "additionalSettingsStep.isAdvancedSettingsVisible" &&
                !values.additionalSettingsStep.isAdvancedSettingsVisible
            ) {
                setValue("additionalSettingsStep.dataDirectory", null);
                setValue("additionalSettingsStep.setupCertificatePath", null);
            }

            switch (name) {
                case "additionalSettingsStep.isAdvancedSettingsVisible": {
                    const state = values.additionalSettingsStep.isAdvancedSettingsVisible ? "enabled" : "disabled";
                    reportEvent(setupWizardGA4Prefixes.additionalSettingsStep, "toggle-advanced", state);
                    break;
                }
                case "additionalSettingsStep.serverEnvironment": {
                    const env = values.additionalSettingsStep.serverEnvironment as string | undefined;
                    if (env) {
                        reportEvent(setupWizardGA4Prefixes.additionalSettingsStep, "set-environment", env);
                    }
                    break;
                }
                case "additionalSettingsStep.adminCertificateExpirationTime": {
                    const months = values.additionalSettingsStep.adminCertificateExpirationTime;
                    if (months != null) {
                        reportEvent(
                            setupWizardGA4Prefixes.additionalSettingsStep,
                            "set-admin-cert-expiration",
                            String(months)
                        );
                    }
                    break;
                }
                case "additionalSettingsStep.postgresqlIntegration": {
                    const enabled = values.additionalSettingsStep.postgresqlIntegration ? "enabled" : "disabled";
                    reportEvent(setupWizardGA4Prefixes.additionalSettingsStep, "toggle-postgresql", enabled);
                    break;
                }
                case "additionalSettingsStep.dataDirectory": {
                    reportEvent(setupWizardGA4Prefixes.additionalSettingsStep, "set-data-dir");
                    break;
                }
                case "additionalSettingsStep.setupCertificatePath": {
                    reportEvent(setupWizardGA4Prefixes.additionalSettingsStep, "set-setup-cert-path");
                    break;
                }
                case "additionalSettingsStep.logsPath": {
                    reportEvent(setupWizardGA4Prefixes.additionalSettingsStep, "set-logs-path");
                    break;
                }
                case "additionalSettingsStep.autoIndexingEngineType": {
                    const type = values.additionalSettingsStep.autoIndexingEngineType as string | undefined;
                    if (type) {
                        reportEvent(setupWizardGA4Prefixes.additionalSettingsStep, "set-auto-indexing-engine", type);
                    }
                    break;
                }
                case "additionalSettingsStep.staticIndexingEngineType": {
                    const type = values.additionalSettingsStep.staticIndexingEngineType as string | undefined;
                    if (type) {
                        reportEvent(setupWizardGA4Prefixes.additionalSettingsStep, "set-static-indexing-engine", type);
                    }
                    break;
                }
            }
        });

        return () => unsubscribe();
    }, [watch, setValue]); // eslint-disable-line react-hooks/exhaustive-deps
}

const serverEnvironmentImg = require("Content/img/setupWizard/studioEnvironment.png");

function ServerEnvironmentSection({
    control,
    licenseInfo,
}: {
    control: Control<SetupWizardFormData>;
    licenseInfo: SetupWizardFormData["licenseKeyStep"]["licenseInfo"];
}) {
    if (getLicenseType(licenseInfo).isHigherThan("Community")) {
        return (
            <FormGroup>
                <FormLabel className="d-flex">
                    <div>Server environment</div>
                    <PopoverWithHoverWrapper
                        message={
                            <PopoverMessage
                                description={
                                    <>
                                        <img src={serverEnvironmentImg} className="mb-2 w-100" />
                                        <span>
                                            Server environment allows you to add a visual identifier to the UI, making
                                            it easier to distinguish between multiple environments when working
                                            simultaneously.
                                        </span>
                                    </>
                                }
                            />
                        }
                        placement="right"
                    >
                        <Icon icon="info-new" />
                    </PopoverWithHoverWrapper>
                </FormLabel>
                <FormSelect
                    control={control}
                    name="additionalSettingsStep.serverEnvironment"
                    options={setupWizardConstants.allServerEnvironmentOptions}
                />
            </FormGroup>
        );
    }

    return null;
}

function CertificateExpirationSection({ control }: { control: Control<SetupWizardFormData> }) {
    return (
        <FormGroup>
            <FormLabel className="d-flex">
                Admin client certificate expiration time
                <PopoverWithHoverWrapper
                    message={
                        <PopoverMessage description="This allows you to define how long the admin client certificate should be valid. By default, this value is set to 60 months." />
                    }
                    placement="right"
                >
                    <Icon icon="info-new" />
                </PopoverWithHoverWrapper>
            </FormLabel>
            <FormInput
                name="additionalSettingsStep.adminCertificateExpirationTime"
                placeholder="default: 60"
                control={control}
                type="number"
                addon="months"
            />
        </FormGroup>
    );
}

interface ExperimentalFeaturesSectionProps {
    control: Control<SetupWizardFormData>;
    licenseInfo: SetupWizardFormData["licenseKeyStep"]["licenseInfo"];
}

function ExperimentalFeaturesSection({ control, licenseInfo }: ExperimentalFeaturesSectionProps) {
    return getLicenseType(licenseInfo).isHigherThan("Community") ? (
        <div>
            <h4 className="mb-0">Experimental features</h4>
            <p className="text-muted">
                Some features, like ones recently released, are considered experimental and are disabled by default.
            </p>
            <PostgreSqlIntegrationToggle control={control} />
        </div>
    ) : null;
}

function PostgreSqlIntegrationToggle({ control }: { control: Control<SetupWizardFormData> }) {
    return (
        <div className="postgresql-integration bg-faded-experimental">
            <FormSwitch name="additionalSettingsStep.postgresqlIntegration" color="experimental" control={control} />
            <div className="d-flex w-100 align-items-center justify-content-between">
                <div className="flex-grow-1 lh-1">
                    <strong className="postgresql-integration__title">
                        PostgreSQL integration
                        <PopoverWithHoverWrapper
                            message={
                                <PopoverMessage description="Enabling this feature allows you to use RavenDB as a PostgreSQL server. You will also need a license that contains PostgreSQL Protocol." />
                            }
                            placement="right"
                        >
                            <Icon icon="info-new" />
                        </PopoverWithHoverWrapper>
                    </strong>
                    <small className="postgresql-integration__description">
                        RavenDB supports the PostgreSQL protocol, enabling tools like Power BI to access its database.
                    </small>
                </div>
                <Icon className="postgresql-integration__icon" size="lg" icon="integrations" />
            </div>
        </div>
    );
}

interface AdvancedSettingsContentProps {
    control: Control<SetupWizardFormData>;
    isVisible: boolean;
}

const getDefaultPath = (os: OperatingSystem, type: "certificate" | "dataDirectory") => {
    const isUnix = os === "Linux" || os === "MacOS";

    const paths = {
        certificate: isUnix
            ? "/etc/ravendb/security/server.pfx"
            : "C:\\RavenDB\\Certificate\\server.pfx",
        dataDirectory: isUnix
            ? "/var/lib/ravendb/data"
            : "C:\\RavenDB\\Data"
    };

    return paths[type];
};


function AdvancedSettingsContent({ control, isVisible }: AdvancedSettingsContentProps) {
    const os = useOS();
    const { resourcesService } = useServices();

    const getLocalFolderPathsProvider = (path: string) => {
        return async () => {
            const dto = await resourcesService.getFolderPathOptions_ServerLocal(path, true);
            return dto?.List || [];
        };
    };

    return (
        <div
            className={classNames({
                "item-disabled": !isVisible,
            })}
        >
            <FormGroup>
                <FormLabel className="hstack">
                    <div>Data directory</div>
                    <ConditionalPopover
                        conditions={{
                            isActive: isVisible,
                            message: <PopoverMessage description="Defines the path to the RavenDB data directory." />,
                        }}
                        popoverPlacement="right"
                    >
                        <Icon icon="info-new" />
                    </ConditionalPopover>
                </FormLabel>
                <FormPathSelector
                    disabled={!isVisible}
                    name="additionalSettingsStep.dataDirectory"
                    control={control}
                    placeholder={getDefaultPath(os, "dataDirectory")}
                    getPathsProvider={(path: string) => getLocalFolderPathsProvider(path)}
                    getPathDependencies={(path: string) => [path]}
                />
            </FormGroup>
            <FormGroup>
                <FormLabel className="hstack">
                    <div>Setup certificate path</div>
                    <ConditionalPopover
                        conditions={{
                            isActive: isVisible,
                            message: <PopoverMessage description="Defines the path to the certificate location." />,
                        }}
                        popoverPlacement="right"
                    >
                        <Icon icon="info-new" />
                    </ConditionalPopover>
                </FormLabel>
                <FormPathSelector
                    disabled={!isVisible}
                    name="additionalSettingsStep.setupCertificatePath"
                    control={control}
                    placeholder={getDefaultPath(os, "certificate")}
                    getPathsProvider={(path: string) => getLocalFolderPathsProvider(path)}
                    getPathDependencies={(path: string) => [path]}
                />
            </FormGroup>
            <FormGroup>
                <FormLabel className="hstack">
                    <div>Logs path</div>
                    <ConditionalPopover
                        conditions={{
                            isActive: isVisible,
                            message: <PopoverMessage description="Defines the path to the logs." />,
                        }}
                        popoverPlacement="right"
                    >
                        <Icon icon="info-new" />
                    </ConditionalPopover>
                </FormLabel>
                <FormPathSelector
                    disabled={!isVisible}
                    name="additionalSettingsStep.logsPath"
                    control={control}
                    placeholder="/logs"
                    getPathsProvider={(path: string) => getLocalFolderPathsProvider(path)}
                    getPathDependencies={(path: string) => [path]}
                />
            </FormGroup>
            <FormGroup>
                <FormLabel className="hstack">
                    <div>Auto indexing engine type</div>
                    <ConditionalPopover
                        conditions={{
                            isActive: isVisible,
                            message: (
                                <PopoverMessage description="Defines the indexing engine used for auto indexes in RavenDB." />
                            ),
                        }}
                        popoverPlacement="right"
                    >
                        <Icon icon="info-new" />
                    </ConditionalPopover>
                </FormLabel>
                <FormSelect
                    className={classNames({ "z-n1": !isVisible })}
                    name="additionalSettingsStep.autoIndexingEngineType"
                    control={control}
                    options={setupWizardConstants.indexingEngineTypeOptions}
                />
            </FormGroup>
            <FormGroup>
                <FormLabel className="hstack">
                    <div>Static indexing engine type</div>
                    <ConditionalPopover
                        conditions={{
                            isActive: isVisible,
                            message: (
                                <PopoverMessage description="Defines the indexing engine used for static indexes in RavenDB." />
                            ),
                        }}
                        popoverPlacement="right"
                    >
                        <Icon icon="info-new" />
                    </ConditionalPopover>
                </FormLabel>
                <FormSelect
                    className={classNames({ "z-n1": !isVisible })}
                    name="additionalSettingsStep.staticIndexingEngineType"
                    control={control}
                    options={setupWizardConstants.indexingEngineTypeOptions}
                />
            </FormGroup>
        </div>
    );
}

export function SetupWizardAdditionalSettingsStepFooter() {
    const { setValue } = useFormContext<SetupWizardFormData>();
    const { reportEvent } = useEventsCollector();

    const handleContinue = () => {
        reportEvent(setupWizardGA4Prefixes.additionalSettingsStep, "continue");
        setValue("currentStep", "Summary");
    };

    const handleBack = () => {
        reportEvent(setupWizardGA4Prefixes.additionalSettingsStep, "back");
        setValue("currentStep", "Node address");
    };

    return (
        <div className="hstack justify-content-between">
            <Button variant="secondary" className="rounded-pill" onClick={handleBack}>
                <Icon icon="arrow-left" /> Back
            </Button>
            <Button variant="primary" className="rounded-pill" onClick={handleContinue}>
                Continue <Icon icon="arrow-right" margin="m-0" />
            </Button>
        </div>
    );
}
