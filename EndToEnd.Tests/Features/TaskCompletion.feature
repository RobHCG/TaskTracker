@EndToEnd
Feature: Task completion notifications
  As a team member
  I want to be notified when a task is completed
  So that I know the work is done

  Scenario: Completing a task creates a notification
    Given I have an open task
    When I complete the task
    Then a notification for the task appears

  Scenario: Completing a reopened task creates another notification
    Given I have an open task
    When I complete the task
    And I reopen the task
    And I complete the task
    Then 2 notifications for the task appear
