import { Control, useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import { Icon } from "components/common/Icon";
import Button from "react-bootstrap/Button";
import { FormGroup, FormInput, FormLabel, FormPathSelector, FormSelect, FormSwitch } from "components/common/Form";
import { setupWizardConstants } from "components/setupWizard/utils/setupWizardConstants";
import { HrHeader } from "components/common/HrHeader";
import classNames from "classnames";
import { useEffect } from "react";
import { getLicenseType } from "components/setupWizard/utils/setupWizardUtils";
import PopoverWithHoverWrapper from "components/common/PopoverWithHoverWrapper";
import { PopoverMessage } from "components/setupWizard/steps/SetupWizardNodeAddressStep";
import { ConditionalPopover } from "components/common/ConditionalPopover";

export function SetupWizardAdditionalSettingsStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { additionalSettingsStep, licenseKeyStep } = useWatch({ control });

    const { licenseInfo } = licenseKeyStep;

    AdditionalSettingsFormSideEffects();

    return (
        <div className="setup-wizard-additional-settings">
            <div className="mb-4">
                <h1>Additional settings</h1>
                <p>At this optional step you may control some of optional settings regarding your setup.</p>
            </div>
            <div>
                <ServerEnvironmentSection control={control} licenseInfo={licenseInfo} />
                <CertificateExpirationSection control={control} />
                <HrHeader />
                <ExperimentalFeaturesSection control={control} licenseInfo={licenseInfo} />
                <div className="d-flex gap-3 flex-column">
                    <div className="d-flex gap-2 align-items-center">
                        <h2 className="mb-0">Advanced</h2>
                        <FormSwitch
                            size="lg"
                            name="additionalSettingsStep.isAdvancedSettingsVisible"
                            control={control}
                        />
                    </div>
                    <p>Settings you may want to consider as an experienced user</p>
                </div>
                <AdvancedSettingsContent
                    control={control}
                    isVisible={additionalSettingsStep.isAdvancedSettingsVisible}
                />
            </div>
        </div>
    );
}

function AdditionalSettingsFormSideEffects() {
    const { setValue, watch, control } = useFormContext<SetupWizardFormData>();

    const { licenseKeyStep } = useWatch({ control });

    useEffect(() => {
        if (getLicenseType(licenseKeyStep.licenseInfo).isDeveloper()) {
            setValue("additionalSettingsStep.serverEnvironment", "Development");
        } else if (getLicenseType(licenseKeyStep.licenseInfo).isProfessionalOrHigher()) {
            setValue("additionalSettingsStep.serverEnvironment", "Production");
        }

        const { unsubscribe } = watch((values, { name }) => {
            if (
                name === "additionalSettingsStep.isAdvancedSettingsVisible" &&
                !values.additionalSettingsStep.isAdvancedSettingsVisible
            ) {
                setValue("additionalSettingsStep.dataDirectory", null);
                setValue("additionalSettingsStep.setupCertificatePath", null);
            }
        });

        return () => unsubscribe();
    }, [watch, setValue]); // eslint-disable-line react-hooks/exhaustive-deps
}

function ServerEnvironmentSection({
    control,
    licenseInfo,
}: {
    control: Control<SetupWizardFormData>;
    licenseInfo: SetupWizardFormData["licenseKeyStep"]["licenseInfo"];
}) {
    return getLicenseType(licenseInfo).isHigherThan("Community") ? (
        <FormGroup>
            <FormLabel className="hstack justify-content-between">
                <div>Server environment</div>
                <PopoverWithHoverWrapper
                    message={
                        <PopoverMessage description="Server environment allows you to add a visual identifier to the UI, making it easier to distinguish between multiple environments when working simultaneously." />
                    }
                >
                    <div className="text-info">
                        <Icon icon="info" size="xs" /> What is this?
                    </div>
                </PopoverWithHoverWrapper>
            </FormLabel>
            <FormSelect
                control={control}
                name="additionalSettingsStep.serverEnvironment"
                options={setupWizardConstants.allServerEnvironmentOptions}
            />
        </FormGroup>
    ) : null;
}

function CertificateExpirationSection({ control }: { control: Control<SetupWizardFormData> }) {
    return (
        <FormGroup>
            <FormLabel className="hstack justify-content-between">
                Admin client certificate expiration time
                <PopoverWithHoverWrapper
                    message={
                        <PopoverMessage description="This allows you to define how long the admin client certificate should be valid. By default, this value is set to 60 months." />
                    }
                >
                    <div className="text-info">
                        <Icon icon="info" size="xs" /> What is this?
                    </div>
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
            <p className="mb-0">Experimental features</p>
            <p>Some features, like ones recently released, are considered experimental and are disabled by default.</p>
            <PostgreSqlIntegrationToggle control={control} />
        </div>
    ) : null;
}

function PostgreSqlIntegrationToggle({ control }: { control: Control<SetupWizardFormData> }) {
    return (
        <div className="postgresql-integration">
            <FormSwitch name="additionalSettingsStep.postgresqlIntegration" control={control} />
            <div className="d-flex w-100 align-items-center justify-content-between">
                <div className="flex-grow-1">
                    <div className="postgresql-integration__title">
                        PostgreSQL integration
                        <PopoverWithHoverWrapper
                            message={
                                <PopoverMessage description="Enabling this feature allows you to use RavenDB as a PostgreSQL server. You will also need a license that contains PostgreSQL Protocol." />
                            }
                        >
                            <Icon icon="info" margin="ms-1" />
                        </PopoverWithHoverWrapper>
                    </div>
                    <div className="postgresql-integration__description">
                        RavenDB supports the PostgreSQL protocol, enabling tools like Power BI to access its database.
                    </div>
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

function AdvancedSettingsContent({ control, isVisible }: AdvancedSettingsContentProps) {
    return (
        <div
            className={classNames({
                "opacity-25 fade": !isVisible,
            })}
        >
            <FormGroup>
                <FormLabel className="hstack justify-content-between">
                    <div>Data directory</div>
                    <ConditionalPopover
                        conditions={{
                            isActive: isVisible,
                            message: <PopoverMessage description="Defines the path to the RavenDB data directory." />,
                        }}
                    >
                        <div className="text-info">
                            <Icon icon="info" size="xs" /> What is this?
                        </div>
                    </ConditionalPopover>
                </FormLabel>
                <FormPathSelector
                    disabled={!isVisible}
                    name="additionalSettingsStep.dataDirectory"
                    control={control}
                    placeholder="/data"
                    getPathsProvider={() => () => Promise.resolve(["C:\\", "D:\\"])}
                    getPathDependencies={(path: string) => [path]}
                />
            </FormGroup>
            <FormGroup>
                <FormLabel className="hstack justify-content-between">
                    <div>Setup certificate path</div>
                    <ConditionalPopover
                        conditions={{
                            isActive: isVisible,
                            message: <PopoverMessage description="Defines the path to the certificate location." />,
                        }}
                    >
                        <div className="text-info">
                            <Icon icon="info" size="xs" /> What is this?
                        </div>
                    </ConditionalPopover>
                </FormLabel>
                <FormPathSelector
                    disabled={!isVisible}
                    name="additionalSettingsStep.setupCertificatePath"
                    control={control}
                    placeholder="/etc/ravendb/security/server.pfx"
                    getPathsProvider={() => () => Promise.resolve(["C:\\", "D:\\"])}
                    getPathDependencies={(path: string) => [path]}
                />
            </FormGroup>
        </div>
    );
}

export function SetupWizardAdditionalSettingsStepFooter() {
    const { setValue } = useFormContext<SetupWizardFormData>();

    const handleContinue = () => {
        setValue("currentStep", "Summary");
    };

    const handleBack = () => {
        setValue("currentStep", "Node address")
    }
    
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
