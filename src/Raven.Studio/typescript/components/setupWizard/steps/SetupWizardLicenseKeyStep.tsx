import { useFormContext } from "react-hook-form";
import { useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import { Icon } from "components/common/Icon";
import Button from "react-bootstrap/Button";
import {
    FormGroup,
    FormInput,
    FormLabel,
    FormCheckbox,
    FormMultiRadioToggle,
    FormSelect,
    OptionalLabel,
} from "components/common/Form";
import useConfirm from "components/common/ConfirmDialog";
import { HStack } from "components/common/HStack";
import { setupWizardConstants } from "../utils/setupWizardConstants";
import Row from "react-bootstrap/Row";
import { useServices } from "components/hooks/useServices";
import ButtonWithSpinner from "components/common/ButtonWithSpinner";
import { useAsyncDebounce } from "components/hooks/useAsyncDebounce";
import Badge from "react-bootstrap/Badge";
import messagePublisher from "common/messagePublisher";

export function SetupWizardLicenseKeyStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const {
        licenseKeyStep: { licenseTypeToGenerate },
    } = useWatch({ control });

    return (
        <div>
            {licenseTypeToGenerate == null && <NoLicenseToGenerate />}
            {licenseTypeToGenerate === "community" && <GenerateCommunity />}
            {licenseTypeToGenerate === "developer" && <GenerateDeveloper />}
        </div>
    );
}

function NoLicenseToGenerate() {
    const { control, setValue } = useFormContext<SetupWizardFormData>();

    return (
        <div>
            <h2>Enter license key</h2>
            <p>You can either use your existing key or generate a free license.</p>
            <FormGroup className="mt-4">
                <FormLabel>Your key</FormLabel>
                <div className="position-relative">
                    <FormInput
                        type="textarea"
                        as="textarea"
                        control={control}
                        name="licenseKeyStep.key"
                        className="rounded-2"
                        placeholder={keyPlaceholder}
                        rows={16}
                        data-testid="license-key-input"
                    />
                    <LicenseKeyBadge />
                </div>
            </FormGroup>
            <div className="mt-2 rounded-2 p-2 panel-bg-1 border border-secondary">
                <h4>Need a new free license? Get it here.</h4>
                <p>Click here to apply for a FREE Community or Developer license for RavenDB.</p>
                <Button
                    variant="outline-success"
                    className="rounded-1"
                    onClick={() => setValue("licenseKeyStep.licenseTypeToGenerate", "community")}
                >
                    Get a new license <Icon icon="arrow-right" />
                </Button>
            </div>
        </div>
    );
}

const keyPlaceholder = `e.g.
{
    "Id": "0cb35dc0-598b-4416-b291-088439082dc1",
    "Name": "RavenDB",
    "Keys": [
        "A1B2C3D4E5F6G7H8I9J0",
        "K1L2M3N4O5P6Q7R8S9T0",
        "U1V2W3X4Y5Z6A7B8C9D0",
        "E1F2G3H4I5J6K7L8M9N0",
        "O1P2Q3R4S5T6U7V8W9X0",
        "Y1Z2A3B4C5D6E7F8G9H0",
        "I1J2K3L4M5N6O7P8Q9R0",
        "S1T2U3V4W5X6Y7Z8A9B0",
        "C1D2E3F4G5H6I7J8K9L0"
    ]
}
`;

function LicenseKeyBadge() {
    const { control } = useFormContext<SetupWizardFormData>();

    const {
        licenseKeyStep: { licenseInfo },
    } = useWatch({ control });

    if (licenseInfo == null || licenseInfo.licenseType == null) {
        return null;
    }

    const bg = (() => {
        // TODO add all types colors
        switch (licenseInfo.licenseType) {
            case "Community":
                return "info";
            case "Developer":
                return "success";
            case "Enterprise":
                return "primary";
            default:
                return "secondary";
        }
    })();

    return (
        <Badge bg={bg} pill className="position-absolute bottom-0 end-0 mb-3 me-3" style={{ zIndex: 5 }}>
            {licenseInfo.licenseType}
        </Badge>
    );
}

function GenerateCommunity() {
    return (
        <div>
            <HStack className="justify-content-between flex-wrap mb-2">
                <h2>
                    Generate new <span className="text-info">Community</span> license
                </h2>
                <LicenseTypeRadio />
            </HStack>
            <p>
                Good for teams who are just starting out and simply want the essentials.
                <br />
                <br />
                <Icon icon="check" color="info" /> Eligible for commercial use
                <br />
                <Icon icon="check" color="info" /> Access to all basic features
                <br />
                <Icon icon="check" color="info" /> Max of 3 nodes in cluster, 3 CPU cores, and 6 GB RAM
            </p>
            <SeeAllPlansButton />
            <GenerateLicenseFields />
        </div>
    );
}

function GenerateDeveloper() {
    return (
        <div>
            <HStack className="justify-content-between flex-wrap mb-2">
                <h2>
                    Generate new <span className="text-success">Developer</span> license
                </h2>
                <LicenseTypeRadio />
            </HStack>
            <p>
                Recommended for teams who want to test & develop RavenDB in its’ full potential.
                <br />
                <br />
                <Icon icon="cancel" color="success" /> Not applicable for commercial use
                <br />
                <Icon icon="check" color="success" /> Enterprise-level set of features
                <br />
                <Icon icon="check" color="success" /> Max of 3 nodes in cluster, 9 CPU cores, and 36 GB RAM
            </p>
            <SeeAllPlansButton />
            <GenerateLicenseFields />
        </div>
    );
}

function GenerateLicenseFields() {
    const { control } = useFormContext<SetupWizardFormData>();

    return (
        <Row className="w-100 mt-4">
            <FormGroup className="col-md-6">
                <FormLabel>First name</FormLabel>
                <FormInput
                    type="text"
                    control={control}
                    name="licenseKeyStep.firstName"
                    placeholder="Your first name"
                />
            </FormGroup>
            <FormGroup className="col-md-6">
                <FormLabel>Last name</FormLabel>
                <FormInput type="text" control={control} name="licenseKeyStep.lastName" placeholder="Your last name" />
            </FormGroup>
            <FormGroup className="col-md-6">
                <FormLabel>Email</FormLabel>
                <FormInput type="email" control={control} name="licenseKeyStep.email" placeholder="Your email" />
            </FormGroup>
            <FormGroup className="col-md-6">
                <FormLabel>
                    Phone <OptionalLabel />
                </FormLabel>
                <FormInput type="text" control={control} name="licenseKeyStep.phone" placeholder="Your phone number" />
            </FormGroup>
            <FormGroup className="col-md-6">
                <FormLabel>Country</FormLabel>
                <FormSelect
                    control={control}
                    options={setupWizardConstants.allCountries.map((x) => ({ value: x, label: x }))}
                    name="licenseKeyStep.country"
                    placeholder="Your country"
                />
            </FormGroup>
            <FormGroup className="col-md-6">
                <FormLabel>Job title</FormLabel>
                <FormSelect
                    control={control}
                    options={setupWizardConstants.allJobTitles.map((x) => ({ value: x, label: x }))}
                    name="licenseKeyStep.jobTitle"
                    placeholder="Choose your job title"
                />
            </FormGroup>
            <FormGroup className="col-md-6">
                <FormLabel>Industry</FormLabel>
                <FormSelect
                    control={control}
                    options={setupWizardConstants.allIndustries.map((x) => ({ value: x, label: x }))}
                    name="licenseKeyStep.industry"
                    placeholder="Choose your industry"
                />
            </FormGroup>
            <FormGroup className="col-md-6">
                <FormLabel>Company</FormLabel>
                <FormInput type="text" control={control} name="licenseKeyStep.company" placeholder="Your company" />
            </FormGroup>
            <FormGroup className="col-md-6">
                <FormLabel>How do you plan to use RavenDB?</FormLabel>
                <FormSelect
                    control={control}
                    options={setupWizardConstants.howYouPlanToUseRavenDBOptions}
                    name="licenseKeyStep.howYouPlanToUseRavenDB"
                    placeholder="Choose the most suitable option"
                />
            </FormGroup>
            <FormGroup className="mt-2">
                <FormCheckbox control={control} name="licenseKeyStep.isAcceptTerms" color="secondary">
                    I accept the terms and conditions
                </FormCheckbox>
            </FormGroup>
            <FormGroup>
                <FormCheckbox control={control} name="licenseKeyStep.isAcceptEmails" color="secondary">
                    I would like to receive learning materials and occasional marketing emails (optional)
                </FormCheckbox>
            </FormGroup>
        </Row>
    );
}

function SeeAllPlansButton() {
    // TODO see all plans link
    return (
        <Button variant="link" className=" p-0">
            See all plans in detail <Icon icon="newtab" />
        </Button>
    );
}

function LicenseTypeRadio() {
    const { control } = useFormContext<SetupWizardFormData>();

    return (
        <div>
            <FormMultiRadioToggle
                control={control}
                name="licenseKeyStep.licenseTypeToGenerate"
                inputItems={[
                    {
                        label: "Community",
                        value: "community",
                        badgeColor: "info",
                        icon: <Icon icon="community" />,
                    },
                    {
                        label: "Developer",
                        value: "developer",
                        badgeColor: "success",
                        icon: <Icon icon="console" />,
                    },
                ]}
                className="d-flex justify-content-center"
            />
        </div>
    );
}

export function SetupWizardLicenseKeyStepFooter() {
    const confirm = useConfirm();
    const { control, setValue, trigger } = useFormContext<SetupWizardFormData>();
    const { setupWizardService } = useServices();

    const {
        licenseKeyStep: { key, licenseTypeToGenerate },
    } = useWatch({ control });

    const asyncRegistrationInfo = useAsyncDebounce(
        async () => {
            setValue("licenseKeyStep.licenseInfo", null);

            if (key == null) {
                return;
            }

            // TODO validate yup schema to make sure it's valid license key
            const info = await setupWizardService.registrationInfo(JSON.parse(key));

            setValue("licenseKeyStep.licenseInfo", {
                licenseType: info.LicenseType,
                userDomainsWithIps: {
                    email: info.UserDomainsWithIps.Emails,
                    rootDomains: info.UserDomainsWithIps.RootDomains,
                    domains: info.UserDomainsWithIps.Domains,
                },
                maxClusterSize: info.MaxClusterSize,
            });
        },
        [key],
        300
    );

    const handleAlreadyHaveLicense = () => {
        setValue("licenseKeyStep.licenseTypeToGenerate", null);
    };

    const handleBack = () => {
        setValue("currentStep", "Setup method");
    };

    const handleGenerateLicense = async () => {
        const isValid = await trigger(["licenseKeyStep"]);

        if (isValid) {
            // TODO generate license from server
            messagePublisher.reportSuccess(`${licenseTypeToGenerate} license successfully generated`);
            setValue("licenseKeyStep.key", "some-generated-key");
            setValue("licenseKeyStep.licenseTypeToGenerate", null);
        }
    };

    const handleContinue = async () => {
        if (key) {
            setValue("currentStep", "Security");
        } else {
            // TODO move to separate component
            const isConfirmed = await confirm({
                title: (
                    <span>
                        <Icon icon="license" color="warning" />
                        You’re about to skip license verification
                    </span>
                ),
                message: (
                    <p>
                        While you’ll be able to use RavenDB, there will be some limitations:
                        <br />
                        <br />
                        <Icon icon="check" color="success" /> AGPLv3 restrictions applied
                        <br />
                        <Icon icon="check" color="success" /> Limited set of features
                        <br />
                        <Icon icon="check" color="success" /> Max of 1 node in cluster, 3 CPU cores, and 6 GB RAM memory
                        usage
                        <br />
                        Either confirm your choice and skip the verification, or generate a new{" "}
                        <Button
                            variant="link"
                            className="text-info p-0 text-decoration-underline"
                            onClick={() => setValue("licenseKeyStep.licenseTypeToGenerate", "community")}
                        >
                            Community
                        </Button>{" "}
                        or{" "}
                        <Button
                            variant="link"
                            className="text-success p-0 text-decoration-underline"
                            onClick={() => setValue("licenseKeyStep.licenseTypeToGenerate", "developer")}
                        >
                            Developer
                        </Button>{" "}
                        license.
                    </p>
                ),
                actionColor: "warning",
                size: "lg",
                confirmText: "Skip verification",
                confirmIcon: "arrow-right",
            });
            if (isConfirmed) {
                setValue("currentStep", "Security");
            }
        }
    };

    return (
        <div className="d-flex justify-content-between">
            {licenseTypeToGenerate != null ? (
                <Button variant="outline-secondary" className="rounded-pill" onClick={handleAlreadyHaveLicense}>
                    <Icon icon="license" /> I already have a license
                </Button>
            ) : (
                <Button variant="secondary" className="rounded-pill" onClick={handleBack}>
                    <Icon icon="arrow-left" /> Back
                </Button>
            )}

            {licenseTypeToGenerate != null ? (
                <Button variant="primary" className="rounded-pill" onClick={handleGenerateLicense}>
                    Generate license <Icon icon="arrow-right" margin="m-0" />
                </Button>
            ) : (
                <ButtonWithSpinner
                    variant="primary"
                    className="rounded-pill"
                    onClick={handleContinue}
                    isSpinning={asyncRegistrationInfo.loading}
                >
                    Continue <Icon icon="arrow-right" margin="m-0" />
                </ButtonWithSpinner>
            )}
        </div>
    );
}
