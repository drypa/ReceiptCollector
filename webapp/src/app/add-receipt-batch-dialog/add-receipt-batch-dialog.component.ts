import {Component, OnInit, ViewChild} from '@angular/core';
import {FormControl, FormGroup} from "@angular/forms";
import {ReceiptService} from "../receipt.service";
import {MatSnackBar} from "@angular/material/snack-bar";
import {CdkTextareaAutosize} from '@angular/cdk/text-field';

@Component({
  selector: 'app-add-receipt-batch-dialog',
  templateUrl: './add-receipt-batch-dialog.component.html',
  styleUrls: ['./add-receipt-batch-dialog.component.scss']
})
export class AddReceiptBatchDialogComponent implements OnInit {
  @ViewChild('autosize') autosize: CdkTextareaAutosize;
  control = new FormControl('');
  newReceiptForm = new FormGroup({
    barCodeText: this.control
  });

  constructor(private receiptService: ReceiptService,
              private snackBar: MatSnackBar) {
  }

  ngOnInit() {
  }

  onSubmit() {
    const receipts = this.control.value
      .split('\n')
      .map(s => s.trim())
      .filter(s => s.length !== 0);

    console.log(receipts);
    this.receiptService.batchAdd(receipts)
      .subscribe(() => {
        this.control.setValue('');
        this.showSnack("Added")
      }, err => this.showSnack("Error"));
  }

  private showSnack(message: string) {
    this.snackBar.open(message, "OK", {})
  }
}
