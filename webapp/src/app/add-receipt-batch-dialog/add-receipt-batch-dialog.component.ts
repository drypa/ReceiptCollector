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
  @ViewChild('autosize', {static: false}) autosize: CdkTextareaAutosize;
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
    console.log(this.control.value.split('\n'));
  }
}
