import { withBootstrap5, withForceRerender, withStorybookContexts } from "test/storybookTestUtils";
import { Meta, StoryObj } from "@storybook/react-webpack5";
import TasksErrorsPage from "components/pages/database/tasks/tasksErrors/TasksErrorsPage";
import { mockStore } from "test/mocks/store/MockStore";
import { mockServices } from "test/mocks/services/MockServices";

export default {
    title: "Pages/Tasks/Tasks Errors",
    decorators: [withStorybookContexts, withBootstrap5, withForceRerender],
    parameters: {
        design: {
            type: "figma",
            url: "https://www.figma.com/design/zOjJhPl1Jk1McZXXBwcw10/Pages---Tasks-Errors?node-id=639-19865&t=RKPuQ28LNLPaRFpl-0",
        },
    },
} satisfies Meta;

export const Default: StoryObj = {
    name: "Tasks Errors",
    render: () => {
        const { databases } = mockStore;
        const {databasesService} = mockServices;
        databases.withActiveDatabase_NonSharded_SingleNode();

        databasesService.withEtlErrors();
        databasesService.withEtlStats();
        return <TasksErrorsPage />;
    },
};